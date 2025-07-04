﻿using FuzzySharp;
using HtmlAgilityPack;
using Newtonsoft.Json;
using System.Text;
using System.Xml;
using static Readaloud_Epub3_Creator.EpubUtility;
using static Readaloud_Epub3_Creator.TranscriptClass;
using static Readaloud_Epub3_Creator.Alingner;
using System.IO;
namespace Readaloud_Epub3_Creator
{
    public class GenerateEpubUtil
    {
        public static void GenerateEpub(Readaloud_Epub3_Creator.AppSettings settings, string folderPath, IProgress<int>? progress = null)
        {

            string device = settings.Device;
            string transcriberPath = settings.TranscriberPath;
            int workers = settings.MaxConcurrentTranscriptions;






            Console.WriteLine("Starting Alingment");
            string epubPath = Path.Combine(folderPath, "OriginalEpub");
            string epubFilePath = GetAllFilesOfType(epubPath, ".epub")[0];
            string epubFolderPathNew = Path.Combine(folderPath, "ProcessedEpub");
            string epubPathNew = Path.GetFullPath(Path.Combine(folderPath, @"..\", "ProcessedBooks", Path.GetFileName(epubFilePath)));
            string audioPath = Path.Combine(folderPath, "Audio");

            // Load and extract
            Dictionary<string, HtmlDocument> htmlDocs = LoadEpubAndExtractHtml(epubFilePath);
            var segments = ExtractAllTextSegments(htmlDocs);
            List<WordSegment> wordSegments = SplitTextSegmentsIntoWords(segments);

            List<string> mp = GetAllFilesOfType(audioPath, ".mp3");
            string jsonFilePath = Path.Combine(audioPath, "transcriptions.json");
            string y = "";

            if (File.Exists(jsonFilePath))
            {
                y = File.ReadAllText(jsonFilePath);
            }
            else
            {
                Console.WriteLine("Running transcriptor on device: " + device);

                string transcriptionOutput = RunTranscription(
                    venvPath: Path.GetFullPath(Path.Combine(transcriberPath, @"..\venv")),
                    scriptPath: transcriberPath,
                    mp3Files: mp.ToArray(),
                    device: device,
                    outputPath: jsonFilePath,
                    workers: workers,
                    onProgress: progress.Report // Link progress to UI
                );

                Console.WriteLine(transcriptionOutput);
                if (File.Exists(jsonFilePath))
                {

                y = File.ReadAllText(jsonFilePath);
                }
                else
                {
                    return;
                }
            }

            List<Root> transcript = JsonConvert.DeserializeObject<List<Root>>(y);
            List<Segment> segments1 = ExtractSegmentsWithFileId(transcript);
            Segment.AssignListIndices(segments1);

            string wordPath = Path.Combine(epubPath, "Words.json");
            List<WordSegment> words = File.Exists(wordPath) ? LoadWordSegments(wordPath) : new List<WordSegment>();
            if (!File.Exists(wordPath))
            {
                Console.WriteLine("running Alingment");
                AlignTranscriptToWords(ref wordSegments, segments1, wordPath);
                words = LoadWordSegments(wordPath);
            }

            WordSegment.AssignListIndices(words);

            var audioGaps = CollectAudioLinkGaps(words);
            FillSegmentGaps(ref words, segments1, audioGaps);


            AssignSentenceIndices(words, htmlDocs);
            TagWordsWithSmilSpans(words);

            Console.WriteLine("generating smil files");
            string smilPath = Path.Combine(epubFolderPathNew, "MediaOverlays");
            GenerateSplitSmilFilesGroupedByFile(words, smilPath);


            List<HtmlTextSegment> recombinedSegments = RecombineWordsIntoTextSegments(words);

            ApplyTextSegmentsToHtmlDocuments(htmlDocs, recombinedSegments);

            var smilFiles = GetAllFilesOfType(smilPath, ".smil");
            RebuildEpubWithMedia(epubFilePath, htmlDocs, smilFiles, mp, epubPathNew);
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
                    .GroupBy(w => string.Join(",", w.LinkedSegments.Select(s => $"{s.fileId}_seg-{s.id}")))
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
            var byFile = words.GroupBy(w => w.FileName);
            int globalSentenceCounter = 0;

            foreach (var fileGroup in byFile)
            {
                string fileName = fileGroup.Key;

                // Group by unique segment ID key
                var bySegmentGroup = fileGroup
                    .GroupBy(w => string.Join(",", w.LinkedSegments.Select(s => $"{s.fileId}_seg-{s.id}")))
                    .ToList();

                foreach (var segmentGroup in bySegmentGroup)
                {
                    // Skip group if all words have no linked segments
                    if (segmentGroup.All(w => w.LinkedSegments == null || w.LinkedSegments.Count == 0))
                        continue;

                    var bySentence = segmentGroup
                        .GroupBy(w => w.SentenceIndex)
                        .OrderBy(g => g.Key)
                        .ToList();

                    foreach (var sentenceGroup in bySentence)
                    {
                        var sentenceWords = sentenceGroup.OrderBy(w => w.IndexInList).ToList();
                        if (sentenceWords.Count == 0)
                            continue;

                        var first = sentenceWords.First();
                        var last = sentenceWords.Last();

                        if (first.SentenceIndex == -1)
                        {

                            // Include both global and local index in the span ID
                            string spanId3 = $"id-sentence{globalSentenceCounter}";

                            first.Word = $"<span id=\"{spanId3}\">{first.Word}";
                            last.Word += "</span>";

                        }
                        else
                        {
                            // Include both global and local index in the span ID
                            string spanId = $"id-sentence{globalSentenceCounter}-{first.SentenceIndex}";

                            first.Word = $"<span id=\"{spanId}\">{first.Word}";
                            last.Word += "</span>";

                        }


                    }
                    globalSentenceCounter++;
                }
            }
        }










        public class AudioLinkGap
        {
            public int StartSegmentIndex { get; set; }
            public int EndSegmentIndex { get; set; }
            public List<WordSegment> AffectedWords { get; set; } = new();
            public bool IsGap { get; set; }

        }
        public static void FillSegmentGaps(ref List<WordSegment> words, List<Segment> segments, List<AudioLinkGap> gaps)
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
                    foreach (var item in gap.AffectedWords)
                    {
                        item.LinkedSegments.Add(segments[gap.StartSegmentIndex]);
                        words[item.IndexInList].LinkedSegments = item.LinkedSegments;
                    }
                    continue;
                }

                foreach (var word in gap.AffectedWords)
                {
                    word.LinkedSegments = new List<Segment>(inBetweenSegments);
                    words[word.IndexInList].LinkedSegments = word.LinkedSegments;
                }
            }
        }




        public static List<AudioLinkGap> CollectAudioLinkGaps(List<WordSegment> words)
        {
            var gaps = new List<AudioLinkGap>();
            AudioLinkGap currentGap = null;

            Segment previousSegment = null;

            for (int i = 0; i < words.Count; i++)
            {
                var word = words[i];
                var currentSegment = word.LinkedSegments != null && word.LinkedSegments.Count > 0
                    ? word.LinkedSegments[0]
                    : null;

                bool isNull = currentSegment == null;
                bool isGap = false;

                if (!isNull && previousSegment != null)
                {
                    int diff = currentSegment.IndexInList - previousSegment.IndexInList;
                    isGap = diff > 1;  // any non-continuous jump
                }

                if (isNull || isGap)
                {
                    if (currentGap == null)
                    {
                        currentGap = new AudioLinkGap
                        {
                            StartSegmentIndex = previousSegment?.IndexInList ?? -1,
                            IsGap = isGap
                        };
                    }

                    currentGap.AffectedWords.Add(word);

                    // end the gap immediately if it's a segment jump
                    if (!isNull && isGap)
                    {
                        currentGap.EndSegmentIndex = currentSegment.IndexInList;
                        gaps.Add(currentGap);
                        currentGap = null;
                    }
                }
                else if (currentGap != null)
                {
                    currentGap.EndSegmentIndex = currentSegment.IndexInList;
                    gaps.Add(currentGap);
                    currentGap = null;
                }

                if (!isNull)
                    previousSegment = currentSegment;
            }

            return gaps;
        }














































    }
}
