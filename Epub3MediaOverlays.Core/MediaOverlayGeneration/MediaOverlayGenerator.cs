using HtmlAgilityPack;
using Newtonsoft.Json;
using System.IO;
using System.Text.RegularExpressions;
using Epub3MediaOverlays.Core.MediaOverlayGeneration.Internal;
using Epub3MediaOverlays.Core.MediaOverlayGeneration.Models;

namespace Epub3MediaOverlays.Core.MediaOverlayGeneration
{
    /// <summary>
    /// Main orchestrator for generating EPUB3 Media Overlays.
    /// 
    /// This class coordinates the entire media overlay creation workflow:
    /// 1. Validates input EPUB and audio files
    /// 2. Extracts text content from EPUB
    /// 3. Generates speech-to-text transcription (or loads cached version)
    /// 4. Aligns audio timestamps to text segments
    /// 5. Generates SMIL synchronization files
    /// 6. Rebuilds EPUB with media overlay integration
    /// 
    /// This is the main PUBLIC API - other classes in this feature are internal implementation.
    /// </summary>
    public class MediaOverlayGenerator
    {
        public MediaOverlayGeneratorSettings Settings { get; set; }

        public MediaOverlayGenerator(MediaOverlayGeneratorSettings settings)
        {
            this.Settings = settings;
        }

        /// <summary>
        /// Generates an EPUB3 file with media overlays (audio synchronization).
        /// </summary>
        public void GenerateEpub(BookData data)
        {
            // 1. Validation & Setup
            if (string.IsNullOrEmpty(data.EpubPath) || !File.Exists(data.EpubPath))
            {
                Console.WriteLine("Error: Source EPUB not found.");
                return;
            }

            Settings.TranscriptionScript.Mp3Files = data.Mp3Paths.ToArray();
            Settings.TranscriptionScript.OutputPath = data.TranscriptionJsonPath;

            // 2. Load and extract EPUB content
            Dictionary<string, HtmlDocument> htmlDocs = EpubProcessor.LoadEpubAndExtractHtml(data.EpubPath);
            var segments = EpubProcessor.ExtractAllTextSegments(htmlDocs);
            List<WordSegment> wordSegments = SplitTextSegmentsIntoWords(segments);
            WordSegment.AssignListIndices(wordSegments);

            // 3. Handle Transcription (Local Cache or Python Script)
            string transcriptionRaw = "";
            if (File.Exists(data.TranscriptionJsonPath))
            {
                transcriptionRaw = File.ReadAllText(data.TranscriptionJsonPath);
            }
            else
            {
                Directory.CreateDirectory(data.AudioPathDir);

                TranscriptProcessor.RunTranscription(
                    Path.GetFullPath(Path.Combine(Settings.TranscriptionScript.TranscriptPath, @"venv")),
                    Settings.TranscriptionScript,
                    line => Console.WriteLine("[Transcription] " + line)
                );

                if (!File.Exists(data.TranscriptionJsonPath)) return;
                transcriptionRaw = File.ReadAllText(data.TranscriptionJsonPath);
            }

            // 4. Process Transcript
            List<TranscriptionRoot> transcript = JsonConvert.DeserializeObject<List<TranscriptionRoot>>(transcriptionRaw)
                ?? throw new ArgumentNullException(nameof(transcriptionRaw));
            foreach (var item in transcript)
            {
                item.LinkSegments();
            }
            List<AudioFragment> audioSegments = TranscriptProcessor.ExtractSegmentsWithFileId(transcript);
            AudioFragment.AssignListIndices(audioSegments);

            // 5. Alignment (Local Cache or Logic)
            List<WordSegment> words;
            if (File.Exists(data.WordsJsonPath))
            {
                words = EpubProcessor.LoadWordSegments(data.WordsJsonPath);
            }
            else
            {
                Console.WriteLine("Running Alignment...");
                var alignment = new AlignmentProcessor(
                    ref wordSegments,
                    ref audioSegments,
                    data.WordsJsonPath,
                    data.AlignmentLogPath,
                    Settings.AlignmentConfig);
                alignment.RunAlignment();
                words = EpubProcessor.LoadWordSegments(data.WordsJsonPath);
            }

            WordSegment.AssignListIndices(words);

            // 6. Refine Segments & SMIL Generation
            var audioGaps = CollectAudioLinkGaps(words);
            FillSegmentGaps(ref words, audioSegments, audioGaps);

            AssignSentenceIndices(words, htmlDocs);
            TagWordsWithSmilSpans(words);

            // Create SMIL files in the temporary processing directory
            string smilPath = Path.Combine(data.TempProcessingFolder, "MediaOverlays");
            Directory.CreateDirectory(smilPath);

            EpubProcessor.NormalizeSegmentsToFullMp3Length(words);
            SmilGenerator.GenerateSmilFiles(words, smilPath);

            // 7. Final Rebuild
            List<HtmlTextSegment> recombinedSegments = EpubProcessor.RecombineWordsIntoTextSegments(words);
            EpubProcessor.ApplyTextSegmentsToHtmlDocuments(htmlDocs, recombinedSegments);

            var smilFiles = EpubProcessor.GetAllFilesOfType(smilPath, ".smil");

            Directory.CreateDirectory(data.GlobalProcessedFolder);

            EpubProcessor.RebuildEpubWithMedia(
                data.EpubPath,
                htmlDocs,
                smilFiles,
                data.Mp3Paths,
                data.FinalEpubOutputPath
            );

            Console.WriteLine("EPUB Generation Complete: " + data.FinalEpubOutputPath);
        }

        /// <summary>
        /// Splits text segments into individual words.
        /// </summary>
        private static List<WordSegment> SplitTextSegmentsIntoWords(List<HtmlTextSegment> segments)
        {
            var wordSegments = new List<WordSegment>();
            var wordRegex = new Regex(@"(\w+|\s+|[^\w\s]+)", RegexOptions.Compiled);

            foreach (var segment in segments)
            {
                string text = segment.EditedText ?? segment.OriginalText;
                var matches = wordRegex.Matches(text);

                for (int i = 0; i < matches.Count; i++)
                {
                    wordSegments.Add(new WordSegment
                    {
                        FileName = segment.FileName,
                        ParentXPath = segment.ParentXPath,
                        TextNodeIndex = segment.TextNodeIndex,
                        Word = matches[i].Value,
                        WordIndexInSegment = i,
                    });
                }
            }

            return wordSegments;
        }

        /// <summary>
        /// Assigns sentence indices to word segments based on HTML structure and audio links.
        /// </summary>
        private static void AssignSentenceIndices(List<WordSegment> words, Dictionary<string, HtmlDocument> DocDict)
        {
            var sortedWords = words.OrderBy(w => w.IndexInList).ToList();
            int globalSentenceCounter = 0;

            var byFile = sortedWords.GroupBy(w => w.FileName);

            foreach (var fileGroup in byFile)
            {
                var bySegmentGroup = fileGroup
                    .GroupBy(w => string.Join(",", w.LinkedSegments.Select(s => $"{s.FileId}_seg-{s.IndexInList}")))
                    .ToList();

                foreach (var segmentGroup in bySegmentGroup)
                {
                    var wordList = segmentGroup.OrderBy(w => w.IndexInList).ToList();

                    if (wordList.Count == 0) continue;

                    int sentenceIndex = globalSentenceCounter;
                    string currentParentXPath = wordList[0].ParentXPath;

                    for (int i = 0; i < wordList.Count; i++)
                    {
                        var word = wordList[i];

                        if (word.ParentXPath != currentParentXPath)
                        {
                            if (!IsFullyContainedSegment(word.ParentXPath, currentParentXPath, wordList))
                            {
                                sentenceIndex++;
                                currentParentXPath = word.ParentXPath;
                            }
                        }

                        word.SentenceIndex = sentenceIndex;
                    }

                    globalSentenceCounter = sentenceIndex + 1;
                }
            }
        }

        private static bool IsFullyContainedSegment(string childXPath, string parentXPath, List<WordSegment> wordList)
        {
            var childWords = wordList
                .Where(w => w.ParentXPath == childXPath)
                .OrderBy(w => w.IndexInList)
                .ToList();

            var parentWords = wordList
                .Where(w => w.ParentXPath == parentXPath)
                .OrderBy(w => w.IndexInList)
                .ToList();

            if (!childWords.Any() || !parentWords.Any())
                return false;

            int childStart = childWords.First().IndexInList;
            int childEnd = childWords.Last().IndexInList;

            int parentStart = parentWords.First().IndexInList;
            int parentEnd = parentWords.Last().IndexInList;

            return childStart >= parentStart && childEnd <= parentEnd;
        }

        /// <summary>
        /// Tags words with SMIL span IDs for synchronization.
        /// </summary>
        private static void TagWordsWithSmilSpans(List<WordSegment> words)
        {
            var byFile = words.GroupBy(w => w.FileName);
            int globalSyncCounter = 0;

            foreach (var fileGroup in byFile)
            {
                var bySegmentGroup = fileGroup
                    .GroupBy(w => string.Join(";", w.LinkedSegments.Select(s => $"{s.FileId}_{s.IndexInList}")))
                    .ToList();

                foreach (var segmentGroup in bySegmentGroup)
                {
                    if (segmentGroup.All(w => w.LinkedSegments == null || w.LinkedSegments.Count == 0))
                        continue;

                    var bySentence = segmentGroup
                        .GroupBy(w => w.SentenceIndex)
                        .OrderBy(g => g.Key)
                        .ToList();

                    foreach (var sentenceGroup in bySentence)
                    {
                        var sentenceWords = sentenceGroup.OrderBy(w => w.IndexInList).ToList();
                        if (sentenceWords.Count == 0) continue;

                        var first = sentenceWords.First();
                        var last = sentenceWords.Last();

                        string spanId = $"id-sentence{globalSyncCounter}";

                        first.Word = $"<span id=\"{spanId}\">{first.Word}";
                        last.Word += "</span>";

                        globalSyncCounter++;
                    }
                }
            }

            Console.WriteLine($"[OK] Tagged {globalSyncCounter} spans across all files.");
        }

        /// <summary>
        /// Collects gaps in audio link coverage where words lack corresponding audio fragments.
        /// </summary>
        private static List<AudioLinkGap> CollectAudioLinkGaps(List<WordSegment> words)
        {
            var gaps = new List<AudioLinkGap>();
            AudioLinkGap? currentGap = null;

            int previousIndex = -1;
            bool hasPrevious = false;

            foreach (var word in words)
            {
                bool hasSegment = TryGetFirstSegmentIndex(word, out int currentIndex);
                bool isGap = hasPrevious && hasSegment && (currentIndex - previousIndex > 1);

                if (!hasSegment || isGap)
                {
                    currentGap ??= new AudioLinkGap
                    {
                        StartSegmentIndex = hasPrevious ? previousIndex : -1,
                        IsGap = isGap
                    };

                    currentGap.AffectedWords.Add(word);

                    if (isGap && hasSegment)
                    {
                        currentGap.EndSegmentIndex = currentIndex;
                        gaps.Add(currentGap);
                        currentGap = null;
                    }
                }
                else if (currentGap != null)
                {
                    currentGap.EndSegmentIndex = currentIndex;
                    gaps.Add(currentGap);
                    currentGap = null;
                }

                if (hasSegment)
                {
                    previousIndex = currentIndex;
                    hasPrevious = true;
                }
            }

            return gaps;
        }

        /// <summary>
        /// Fills gaps in audio links by assigning nearby audio fragments to unlinked words.
        /// </summary>
        private static void FillSegmentGaps(ref List<WordSegment> words, List<AudioFragment> segments, List<AudioLinkGap> gaps)
        {
            foreach (var gap in gaps)
            {
                if (gap.StartSegmentIndex == -1 || gap.EndSegmentIndex == -1)
                    continue;

                if (gap.EndSegmentIndex < gap.StartSegmentIndex)
                {
                    Console.WriteLine($"[Error] Invalid segment index range: {gap.StartSegmentIndex} to {gap.EndSegmentIndex}");
                    continue;
                }

                var inBetweenSegments = segments
                    .Where(s => s.IndexInList >= gap.StartSegmentIndex + 1 && s.IndexInList <= gap.EndSegmentIndex - 1)
                    .ToList();

                if (inBetweenSegments.Count == 0)
                {
                    var fallbackSegment = segments[gap.StartSegmentIndex];
                    foreach (var item in gap.AffectedWords)
                    {
                        if (item.LinkedSegments.Count > 0 && item.LinkedSegments[0].FileId != fallbackSegment.FileId)
                        {
                            item.LinkedSegments = new List<AudioFragment> { fallbackSegment };
                        }
                        else if (!item.LinkedSegments.Any(s => s.IndexInList == fallbackSegment.IndexInList))
                        {
                        }

                        words[item.IndexInList].LinkedSegments = item.LinkedSegments;
                    }
                    continue;
                }

                var segmentsByFile = inBetweenSegments.GroupBy(s => s.FileId).ToList();
                var validFileGroup = segmentsByFile.First().ToList();

                foreach (var word in gap.AffectedWords)
                {
                    word.LinkedSegments = new List<AudioFragment>(validFileGroup);
                    words[word.IndexInList].LinkedSegments = word.LinkedSegments;
                }
            }
        }

        private static bool TryGetFirstSegmentIndex(WordSegment word, out int index)
        {
            if (word.LinkedSegments != null && word.LinkedSegments.Count > 0)
            {
                index = word.LinkedSegments[0].IndexInList;
                return true;
            }

            index = default;
            return false;
        }
    }
}
