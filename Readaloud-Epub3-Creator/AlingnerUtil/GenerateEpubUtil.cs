using HtmlAgilityPack;
using Newtonsoft.Json;
using Readaloud_Epub3_Creator.AlingnerUtil;
using System.IO;
using static Readaloud_Epub3_Creator.AlingnerUtil.AlingnerNew;
using static Readaloud_Epub3_Creator.EpubUtility;
using static Readaloud_Epub3_Creator.TranscriptClass;
namespace Readaloud_Epub3_Creator
{
    public class GenerateEpubUtilSettings
    {
        public required ITranscriptionScript TranscriptionScript { get; set; }
    }
    public class GenerateEpubUtil
    {
        public GenerateEpubUtilSettings Settings { get; set; }
        public GenerateEpubUtil(GenerateEpubUtilSettings settings)
        {
            this.Settings = settings;
        }
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
            Dictionary<string, HtmlDocument> htmlDocs = LoadEpubAndExtractHtml(data.EpubPath);
            var segments = ExtractAllTextSegments(htmlDocs);
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

                // Ensure the directory for the output exists
                Directory.CreateDirectory(data.AudioPathDir);

                RunTranscription(
                    Path.GetFullPath(Path.Combine(Settings.TranscriptionScript.TranscriptPath, @"venv")),
                    Settings.TranscriptionScript,
                    line => Console.WriteLine("[Transcription] " + line)
                );

                if (!File.Exists(data.TranscriptionJsonPath)) return;
                transcriptionRaw = File.ReadAllText(data.TranscriptionJsonPath);
            }

            // 4. Process Transcript
            List<Root> transcript = JsonConvert.DeserializeObject<List<Root>>(transcriptionRaw) ?? throw new ArgumentNullException(nameof(transcriptionRaw));
            foreach (var item in transcript)
            {
                item.LinkSegments();
            }
            List<Fragment> audioSegments = ExtractSegmentsWithFileId(transcript);
            Fragment.AssignListIndices(audioSegments);

            // 5. Alignment (Local Cache or Logic)
            List<WordSegment> words;
            if (File.Exists(data.WordsJsonPath))
            {
                words = LoadWordSegments(data.WordsJsonPath);
            }
            else
            {
                Console.WriteLine("Running Alignment...");
                var aling = new AlingnerNew(ref wordSegments,ref audioSegments, data.WordsJsonPath,data.AlignmentLogPath);
                aling.RunAlingment();
                words = LoadWordSegments(data.WordsJsonPath);
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

            NormalizeSegmentsToFullMp3Length(words);
            SmilGenerator.GenerateSmilFiles(words, smilPath);

            // 7. Final Rebuild
            List<HtmlTextSegment> recombinedSegments = RecombineWordsIntoTextSegments(words);
            ApplyTextSegmentsToHtmlDocuments(htmlDocs, recombinedSegments);

            var smilFiles = GetAllFilesOfType(smilPath, ".smil");

            // Ensure the global ProcessedBooks folder exists before saving
            Directory.CreateDirectory(data.GlobalProcessedFolder);

            RebuildEpubWithMedia(
                data.EpubPath,
                htmlDocs,
                smilFiles,
                data.Mp3Paths,
                data.FinalEpubOutputPath
            );

            Console.WriteLine("EPUB Generation Complete: " + data.FinalEpubOutputPath);
        }




        public static Dictionary<string, string> ForceUpdateOuterHtml(Dictionary<string, HtmlDocument> htmlDocuments)
        {
            var updatedHtmls = new Dictionary<string, string>();

            foreach (var kvp in htmlDocuments)
            {
                string key = kvp.Key;
                HtmlDocument doc = kvp.Value;

                using (var stringWriter = new StringWriter())
                {
                    // Force-save the document to refresh the underlying HTML structure
                    doc.Save(stringWriter);
                    string updatedHtml = stringWriter.ToString();

                    // Store the forcibly updated OuterHtml
                    updatedHtmls[key] = updatedHtml;
                }
            }

            return updatedHtmls;
        }


























        public static List<string> GetAllFilesOfType(string folderPath, string extension)
        {
            if (!Directory.Exists(folderPath))
                throw new DirectoryNotFoundException($"Folder not found: {folderPath}");

            // Normalize the extension (e.g., ".mp3")
            if (!extension.StartsWith("."))
                extension = "." + extension;

            return new List<string>(
                Directory.GetFiles(folderPath, "*" + extension, SearchOption.AllDirectories)
            );
        }


        public static void AssignSentenceIndices(List<WordSegment> words, Dictionary<string, HtmlDocument> DocDict)
        {
            var sortedWords = words.OrderBy(w => w.IndexInList).ToList();
            int globalSentenceCounter = 0;

            var byFile = sortedWords.GroupBy(w => w.FileName);

            foreach (var fileGroup in byFile)
            {
                string fileName = fileGroup.Key;

                // Group by segment group (based on linked segments)
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
                            // Determine if the current word's parent is fully contained in the previous
                            if (!IsFullyContainedSegment(word.ParentXPath, currentParentXPath, wordList))
                            {
                                // Not fully contained → start new sentence
                                sentenceIndex++;
                                currentParentXPath = word.ParentXPath;
                            }
                            // else → continue with the current sentenceIndex
                        }

                        word.SentenceIndex = sentenceIndex;
                    }

                    globalSentenceCounter = sentenceIndex + 1;
                }
            }
        }

        private static bool IsFullyContainedSegment(string childXPath, string parentXPath, List<WordSegment> wordList)
        {
            // Get all words for the child XPath
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


        public static void TagWordsWithSmilSpans(List<WordSegment> words)
        {
            // Group by file first
            var byFile = words.GroupBy(w => w.FileName);

            // This counter must start at 0 and increment exactly 
            // the same way as the SmilGenerator to ensure IDs match.
            int globalSyncCounter = 0;

            foreach (var fileGroup in byFile)
            {
                // 1. Group by the unique audio segment (matching SmilGenerator logic)
                var bySegmentGroup = fileGroup
                    .GroupBy(w => string.Join(";", w.LinkedSegments.Select(s => $"{s.FileId}_{s.IndexInList}")))
                    .ToList();

                foreach (var segmentGroup in bySegmentGroup)
                {
                    // Skip words that aren't linked to audio
                    if (segmentGroup.All(w => w.LinkedSegments == null || w.LinkedSegments.Count == 0))
                        continue;

                    // 2. Group by sentence index within this segment
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

                        // 3. Apply the simplified naming scheme
                        // ID format: id-sentence0, id-sentence1, etc.
                        string spanId = $"id-sentence{globalSyncCounter}";

                        // Inject the span tags into the Word strings
                        first.Word = $"<span id=\"{spanId}\">{first.Word}";
                        last.Word += "</span>";

                        // 4. Increment the counter for every sync unit (sentence group)
                        globalSyncCounter++;
                    }
                }
            }

            Console.WriteLine($"[OK] Tagged {globalSyncCounter} spans across all files.");
        }
    
        









        public class AudioLinkGap
        {
            public int StartSegmentIndex { get; set; }
            public int EndSegmentIndex { get; set; }
            public List<WordSegment> AffectedWords { get; set; } = new();
            public bool IsGap { get; set; }

        }
        public static void FillSegmentGaps(ref List<WordSegment> words, List<Fragment> segments, List<AudioLinkGap> gaps)
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

                // 1. Get all segments physically located between the start and end of the gap
                var inBetweenSegments = segments
                    .Where(s => s.IndexInList >= gap.StartSegmentIndex + 1 && s.IndexInList <= gap.EndSegmentIndex - 1)
                    .ToList();

                // Case A: No segments found between the gap indices
                if (inBetweenSegments.Count == 0)
                {
                    var fallbackSegment = segments[gap.StartSegmentIndex];
                    foreach (var item in gap.AffectedWords)
                    {
                        // Ensure we don't mix FileIds if the word already had segments
                        if (item.LinkedSegments.Count > 0 && item.LinkedSegments[0].FileId != fallbackSegment.FileId)
                        {
                            // If FileId differs, we overwrite to maintain consistency
                            item.LinkedSegments = new List<Fragment> { fallbackSegment };
                        }
                        else if (!item.LinkedSegments.Any(s => s.IndexInList == fallbackSegment.IndexInList))
                        {
                            item.LinkedSegments.Add(fallbackSegment);
                        }

                        words[item.IndexInList].LinkedSegments = item.LinkedSegments;
                    }
                    continue;
                }

                // Case B: Segments exist in the gap. 
                // We must group them by FileId to satisfy the requirement that a word 
                // only links to segments with the same FileId.
                var segmentsByFile = inBetweenSegments.GroupBy(s => s.FileId).ToList();

                // We take the first group of segments (the first FileId encountered)
                // This ensures the list assigned to the word is homogeneous.
                var validFileGroup = segmentsByFile.First().ToList();

                foreach (var word in gap.AffectedWords)
                {
                    // Assign the homogeneous list to the word
                    word.LinkedSegments = new List<Fragment>(validFileGroup);

                    // Sync with the main list
                    words[word.IndexInList].LinkedSegments = word.LinkedSegments;
                }
            }
        }




        public static List<AudioLinkGap> CollectAudioLinkGaps(List<WordSegment> words)
        {
            var gaps = new List<AudioLinkGap>();
            AudioLinkGap? currentGap = null;

            int previousIndex = -1;   // -1 = no previous segment
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

                    // close immediately on jump
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
