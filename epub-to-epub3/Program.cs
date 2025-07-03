using FuzzySharp;
using HtmlAgilityPack;
using Newtonsoft.Json;
using System.Text;
using System.Xml;
using static Readaloud_Epub3_Creator.EpubUtility;
using static Readaloud_Epub3_Creator.TranscriptClass;
using Readaloud_Epub3_Creator;
namespace Readaloud_Epub3_Creator
{
    public class Program
    {
        static async Task Main(string[] args)
        {



            //string epubPath = "C:\\Users\\TIGO\\source\\repos\\halmel\\epub-to-epub3\\epub-to-epub3\\Pratchett, Terry - [Discworld 10] - Moving Pictures (1990, HarperCollins, 9780061440618).epub";
            //string epubPathNew = "C:\\Users\\TIGO\\source\\repos\\halmel\\epub-to-epub3\\epub-to-epub3\\NewTest.epub";
            //// Load and extract
            //var htmlDocs = EpubUtility.LoadEpubAndExtractHtml(epubPath);


            //var clonedDocs = CloneHtmlDocuments(htmlDocs);

            //// Extract and modify
            //var segments = ExtractAllTextSegments(clonedDocs);







            //var wordSegments = SplitTextSegmentsIntoWords(segments);











            //string y = await File.ReadAllTextAsync("C:\\Users\\TIGO\\source\\repos\\halmel\\epub-to-epub3\\epub-to-epub3\\transcriptions.json");
            //List<TranscriptClass.Root> transcript = JsonConvert.DeserializeObject<List<TranscriptClass.Root>>(y);
            //List<Segment> segments1 = ExtractSegmentsWithFileId(transcript);
            //Segment.AssignListIndices(segments1);

            //AlignTranscriptToWords(ref wordSegments, segments1, "C:\\Users\\TIGO\\source\\repos\\halmel\\epub-to-epub3\\epub-to-epub3\\Words.json");
            //List<WordSegment> words = LoadWordSegments("C:\\Users\\TIGO\\source\\repos\\halmel\\epub-to-epub3\\epub-to-epub3\\Words.json");
            //WordSegment.AssignListIndices(words);


            //var x = CollectAudioLinkGaps(words);
            //FillSegmentGaps(ref words, segments1, x);
            //AssignSentenceIndices(words, htmlDocs);


            //GenerateSplitSmilFilesGroupedByFile(words);

            //TagWordsWithSmilSpans(words);



            //var e = EpubUtility.RecombineWordsIntoTextSegments(words);


            //Console.WriteLine();


            //EpubUtility.ApplyTextSegmentsToHtmlDocuments(htmlDocs, e);


            //Console.WriteLine();

            //var mp = GetAllFilesOfType("C:\\Users\\TIGO\\source\\repos\\halmel\\epub-to-epub3\\epub-to-epub3\\10-MovingPictures\\", ".mp3");
            //var sm = GetAllFilesOfType("C:\\Users\\TIGO\\source\\repos\\halmel\\epub-to-epub3\\epub-to-epub3\\bin\\Debug\\net9.0\\output", ".smil");


            //RebuildEpubWithMedia(epubPath, htmlDocs, sm, mp, epubPathNew);

            //Console.WriteLine();

            //Console.WriteLine("hello");
            //Console.ReadLine();
        }
        public static void GenerateEpub(Readaloud_Epub3_Creator.SettingsManager.AppSettings settings, string folderPath, IProgress<int>? progress = null,)
        {
            Console.WriteLine("Starting Alingment");
            string epubPath = Path.Combine(folderPath, "OriginalEpub");
            string epubFilePath = GetAllFilesOfType(epubPath, ".epub")[0];
            string epubFolderPathNew = Path.Combine(folderPath, "ProcessedEpub");
            string epubPathNew = Path.Combine(folderPath, "ProcessedEpub", Path.GetFileName(epubFilePath));
            string audioPath = Path.Combine(folderPath, "Audio");

            // Load and extract
            Dictionary<string, HtmlDocument> htmlDocs = EpubUtility.LoadEpubAndExtractHtml(epubFilePath);
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
                    venvPath:Path.GetFullPath( Path.Combine(transcriberPath, @"..\venv")),
                    scriptPath: transcriberPath,
                    mp3Files: mp.ToArray(),
                    device: device,
                    outputPath: jsonFilePath,
                    workers: workers,
                    onProgress: progress.Report // Link progress to UI
                );

                Console.WriteLine(transcriptionOutput);
                y = File.ReadAllText(jsonFilePath);
            }

            List<TranscriptClass.Root> transcript = JsonConvert.DeserializeObject<List<TranscriptClass.Root>>(y);
            List<Segment> segments1 = ExtractSegmentsWithFileId(transcript);
            Segment.AssignListIndices(segments1);

            string wordPath = Path.Combine(epubPath, "Words.json");
            List<WordSegment> words = File.Exists(wordPath) ? LoadWordSegments(wordPath) : new List<WordSegment>();

            WordSegment.AssignListIndices(wordSegments);
            if (!File.Exists(wordPath))
            {
                AlignTranscriptToWords(ref wordSegments, segments1, wordPath);
                words = LoadWordSegments(wordPath);
            }

            WordSegment.AssignListIndices(words);

            var audioGaps = CollectAudioLinkGaps(words);
            FillSegmentGaps(ref words, segments1, audioGaps);
            AssignSentenceIndices(words, htmlDocs);

            string smilPath = Path.Combine(epubFolderPathNew, "MediaOverlays");
            GenerateSplitSmilFilesGroupedByFile(words, smilPath);
            TagWordsWithSmilSpans(words);

            var editedSegments = CollectAudioLinkGaps(words);
            var recombinedSegments = EpubUtility.RecombineWordsIntoTextSegments(words);

            EpubUtility.ApplyTextSegmentsToHtmlDocuments(htmlDocs, recombinedSegments);

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




        public static void AlignTranscriptToWords(ref List<WordSegment> words, List<Segment> segments, string wordPath)
        {
            Dictionary<Segment, List<WordSegment>> alignment = AlignSegmentsToWords(segments, words);

            foreach (var kvp in alignment)
            {
                var segment = kvp.Key;
                var matchedWords = kvp.Value;

                if (matchedWords.Count == 0)
                    continue;

                var sentenceGroups = SplitBySentence(matchedWords);
                foreach (var group in sentenceGroups)
                {
                    foreach (var word in group)
                    {
                        word.LinkedSegments.Add(segment);
                    }
                }
            }
            SaveWordSegments(words, wordPath);
        }

        public static void AssignSentenceIndices(List<WordSegment> words, Dictionary<string, HtmlDocument> DocDict)
        {
            var byFile = words.GroupBy(w => w.FileName);

            foreach (var fileGroup in byFile)
            {
                var doc = DocDict[fileGroup.Key];
                var sortedWords = fileGroup.OrderBy(w => w.IndexInList).ToList();

                List<List<WordSegment>> contextualGroups = new();

                for (int i = 0; i < sortedWords.Count; i++)
                {
                    var currentWord = sortedWords[i];
                    var currentNode = doc.DocumentNode.SelectSingleNode(currentWord.ParentXPath);
                    if (currentNode == null) continue;

                    if (contextualGroups.Count == 0)
                    {
                        contextualGroups.Add(new List<WordSegment> { currentWord });
                        continue;
                    }

                    var lastGroup = contextualGroups.Last();
                    var previousWord = lastGroup.Last();
                    var previousNode = doc.DocumentNode.SelectSingleNode(previousWord.ParentXPath);

                    // Look ahead for next node (if any)
                    HtmlNode nextNode = null;
                    if (i + 1 < sortedWords.Count)
                    {
                        var nextWord = sortedWords[i + 1];
                        nextNode = doc.DocumentNode.SelectSingleNode(nextWord.ParentXPath);
                    }

                    // If previous and next (if exists) share the same parent as current → continue group
                    bool sameAsPrevious = previousNode != null && previousNode.ParentNode == currentNode.ParentNode;
                    bool sameAsNext = nextNode != null && nextNode.ParentNode == currentNode.ParentNode;

                    if (sameAsPrevious || sameAsNext)
                    {
                        lastGroup.Add(currentWord);
                    }
                    else
                    {
                        contextualGroups.Add(new List<WordSegment> { currentWord });
                    }
                }

                // Assign SentenceIndex within each contextual group
                int globalSentenceIndex = 0;
                foreach (var group in contextualGroups)
                {
                    var sentenceGroups = SplitBySentence(group);
                    foreach (var sentence in sentenceGroups)
                    {
                        if (sentenceGroups.Count > 1)
                        {
                            foreach (var word in sentence)
                                word.SentenceIndex = globalSentenceIndex;
                            globalSentenceIndex++;
                        }
                        else
                        {
                            sentence[0].SentenceIndex = -1;
                        }
                    }
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


















        private static List<List<WordSegment>> SplitBySentence(List<WordSegment> words)
        {
            return words
                .GroupBy(w => w.ParentXPath)
                .Select(g => g.ToList())
                .ToList();
        }
        public static int startPos = 0;

        private static List<WordSegment> FindMatchingWordSequence(
            List<WordSegment> words,
            string targetText,
            ref int startPos,
                        int scoreThreshold,
                        int ExplorationLimit=10)
        {
            if (startPos < 0 || startPos >= words.Count)
                startPos = 0;

            int maxStart = words.Count - 1;
            int[] bestMatch = new int[2];
            int bestScore = 0;
            int exploration = 0;

            for (int start = startPos; start < maxStart; start++)
            {




                int length = 1;
                var sb = new StringBuilder();

                // Build up candidate string without unnecessary allocations
                while (start + length <= words.Count && sb.Length <= targetText.Length)
                {
                    sb.Append(words[start + length - 1].Word);
                    length++;
                }

                length--; // Go back to last valid length
                if (length <= 0) continue;

                int score = ScoreMatch(words, start, length, targetText);

                if (score > 40)
                {
                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestMatch[0] = start;
                        bestMatch[1] = length;
                        exploration = 0;

                        if (score > 90) break; // Very good match, early exit
                    }
                    else
                    {
                        exploration++;
                    }
                }
                else
                {
                    if (bestScore > 50)
                    {
                        exploration++;
                    }
                }

                if (exploration > 10)
                {
                    break; // No improvement recently, exit search
                }
            }
            if (bestScore > scoreThreshold)
            {
                bestMatch = RefineMatchLength(words, bestScore, bestMatch[0], bestMatch[1], 2, targetText);
                startPos = bestMatch[0] + bestMatch[1];
                return words.Skip(bestMatch[0]).Take(bestMatch[1]).ToList();

            }
            else
            {
                return new List<WordSegment>();
            }
        }



        private static int ScoreMatch(List<WordSegment> words, int start, int length, string target)
        {
            var candidate = string.Join(" ", words.Skip(start).Take(length).Select(w => w.Word));
            return Fuzz.Ratio(candidate, target);
        }



        private static string Normalize(string s)
        {
            var sb = new StringBuilder(s.Length);
            foreach (char c in s)
            {
                if (char.IsLetter(c) || char.IsWhiteSpace(c))
                    sb.Append(char.ToLowerInvariant(c));
            }
            return sb.ToString();
        }

        private static int ScoreStringSimilarity(string a, string b)
        {

            return FuzzySharp.Fuzz.Ratio(Normalize(a), Normalize(b));
        }






        public static Dictionary<Segment, List<WordSegment>> AlignSegmentsToWords(
            List<Segment> segments,
            List<WordSegment> words,
            int initialMatchCount = 3,
            int scoreThreshold = 65,
            int maxLookahead = 2,
            int maxDistanceAhead = 1000)
        {
            var result = new Dictionary<Segment, List<WordSegment>>();
            var pendingMatches = new Dictionary<Segment, List<WordSegment>>();

            int startPos = 0;
            int lastCommittedStartPos = 0;
            int uncommittedMatchCount = 0;
            int uncommittedNoMatchCount = 0;

            const int noMatchTolerance = 20;
            const int commitThreshold = 3;

            for (int i = 0; i < segments.Count; i++)
            {
                var currentSegment = segments[i];
                List<WordSegment> matched = null;

                // === Try Fast Match ===
                Dictionary<Segment, List<WordSegment>> fastMatch = TryFastMatch(segments, i, words, startPos, scoreThreshold, maxLookahead);
                if (fastMatch != null)
                {
                    foreach (var kvp in fastMatch)
                    {
                        pendingMatches[kvp.Key] = kvp.Value;
                        startPos += kvp.Value.Count;
                        uncommittedMatchCount++;

                        // Log match info including IndexInList
                        Console.ForegroundColor = ConsoleColor.DarkCyan;
                        Console.WriteLine($"[FastMatch] Segment {kvp.Key.id}: \"{kvp.Key.text}\"");
                        Console.ForegroundColor = ConsoleColor.Green;
                        Console.WriteLine($"Match:      \"{string.Join(" ", kvp.Value.Select(w => w.Word))}\"");
                        Console.ForegroundColor = ConsoleColor.DarkGray;
                        if (kvp.Value != null && kvp.Value.Count > 0)
                        {
                            Console.WriteLine($"Start index: {kvp.Value.First().IndexInList}");
                        }
                        else
                        {
                            Console.ForegroundColor = ConsoleColor.DarkGray;
                            Console.WriteLine("Start index: [No match found]");
                            Console.ResetColor();
                        }

                        Console.ResetColor();
                    }

                    i += fastMatch.Count - 1;

                    if (result.Count >= initialMatchCount)
                        uncommittedNoMatchCount = 0;

                    if (uncommittedMatchCount >= commitThreshold)
                    {
                        foreach (var kvp in pendingMatches)
                            result[kvp.Key] = kvp.Value;

                        pendingMatches.Clear();
                        lastCommittedStartPos = startPos;
                        uncommittedMatchCount = 0;
                        uncommittedNoMatchCount = 0;

                        Console.ForegroundColor = ConsoleColor.Blue;
                        Console.WriteLine("[Commit] Matches committed after reaching stability.");
                        Console.ResetColor();
                    }

                    continue;
                }

                // === Try Slow Match ===
                var slowMatch = TrySlowMatch(segments, i, words, ref startPos,10, maxDistanceAhead, scoreThreshold);
                if (slowMatch != null)
                {
                    pendingMatches[currentSegment] = slowMatch;
                    startPos += slowMatch.Count;
                    uncommittedMatchCount++;
                    uncommittedNoMatchCount = 0;

                    Console.ForegroundColor = ConsoleColor.Cyan;
                    Console.WriteLine($"[SlowMatch] Segment {currentSegment.id}: \"{currentSegment.text}\"");
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine($"Match:      \"{string.Join(" ", slowMatch.Select(w => w.Word))}\"");
                    Console.ForegroundColor = ConsoleColor.DarkGray;
                    Console.WriteLine($"Start index: {slowMatch.First().IndexInList}");
                    Console.ResetColor();

                    if (uncommittedMatchCount >= commitThreshold)
                    {
                        foreach (var kvp in pendingMatches)
                            result[kvp.Key] = kvp.Value;

                        pendingMatches.Clear();
                        lastCommittedStartPos = startPos;
                        uncommittedMatchCount = 0;
                        uncommittedNoMatchCount = 0;

                        Console.ForegroundColor = ConsoleColor.Blue;
                        Console.WriteLine("[Commit] Matches committed after reaching stability.");
                        Console.ResetColor();
                    }

                    continue;
                }

                // === No Match ===
                result[currentSegment] = new List<WordSegment>();
                uncommittedNoMatchCount++;

                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine($"\n[Warning] No match for segment {currentSegment.id}: \"{currentSegment.text}\"");
                Console.ResetColor();

                // === Check if rollback is needed ===
                if ((uncommittedMatchCount > 0 && uncommittedNoMatchCount >= noMatchTolerance) ||
                    uncommittedNoMatchCount > noMatchTolerance * 2)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"\n[Recovery] ⚠ Alignment drift detected (matches: {uncommittedMatchCount}, no-matches: {uncommittedNoMatchCount}). Attempting rollback recovery...");
                    Console.ResetColor();

                    startPos = lastCommittedStartPos;
                    foreach (var key in pendingMatches.Keys)
                        result.Remove(key);

                    pendingMatches.Clear();
                    uncommittedMatchCount = 0;
                    uncommittedNoMatchCount = 0;

                    const int recoveryWindow = 15;
                    bool recoverySucceeded = false;

                    for (int skipAhead = 0; skipAhead <= recoveryWindow; skipAhead++)
                    {
                        int recoveryIndex = i + skipAhead;
                        if (recoveryIndex >= segments.Count)
                            break;

                        var recoverySegment = segments[recoveryIndex];

                        Console.WriteLine($"[Recovery] Trying to realign at Segment {recoverySegment.id} (index {recoveryIndex})...");

                        var recoveryMatch = TrySlowMatch(segments, recoveryIndex, words, ref startPos,2000, maxDistanceAhead, scoreThreshold, 200);
                        if (recoveryMatch != null)
                        {
                            result[recoverySegment] = recoveryMatch;

                            Console.ForegroundColor = ConsoleColor.Green;
                            Console.WriteLine($"[Recovery] ✅ Re-alignment succeeded at Segment {recoverySegment.id}. Skipped {skipAhead} segments.");
                            Console.WriteLine($"Match:      \"{string.Join(" ", recoveryMatch.Select(w => w.Word))}\"");
                            Console.WriteLine($"Start index: {recoveryMatch.First().IndexInList}");
                            Console.ResetColor();

                            i = recoveryIndex;
                            recoverySucceeded = true;
                            break;
                        }
                    }

                    if (!recoverySucceeded)
                    {
                        Console.ForegroundColor = ConsoleColor.DarkRed;
                        Console.WriteLine("[Recovery] ❌ Could not recover alignment. Proceeding with next segment.");
                        Console.ResetColor();
                    }
                }
            }

            foreach (var kvp in pendingMatches)
                result[kvp.Key] = kvp.Value;

            return result;
        }




        private static Dictionary<Segment, List<WordSegment>> TryFastMatch(
            List<Segment> segments,
            int currentIndex,
            List<WordSegment> words,
            int startPos,
            int scoreThreshold,
            int maxLookahead)
        {
            var tempResult = new Dictionary<Segment, List<WordSegment>>();
            int localStartPos = startPos;

            Segment currentSegment = segments[currentIndex];
            int currentBestScore = 0;
            int currentBestLength = 0;
            string candidateText = "";

            // Try matching the current segment
            for (int len = 1; localStartPos + len <= words.Count; len++)
            {
                candidateText = string.Concat(words.Skip(localStartPos).Take(len).Select(w => w.Word));
                int score = ScoreStringSimilarity(candidateText, currentSegment.text);

                if (score > currentBestScore)
                {
                    currentBestScore = score;
                    currentBestLength = len;
                }

                if (candidateText.Length > currentSegment.text.Length + 10)
                    break;
            }

            candidateText = string.Concat(words.Skip(localStartPos).Take(currentBestLength).Select(w => w.Word));
            string testText = string.Concat(words.Skip(localStartPos - 10).Take(50).Select(w => w.Word));

            // If match is already good, accept
            if (currentBestScore >= scoreThreshold && currentBestLength > 0)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"\n[FastMatch] ✅ Direct match for Segment {currentSegment.id},{currentIndex} | Score: {currentBestScore}");
                Console.ForegroundColor = ConsoleColor.Cyan;
                Console.WriteLine($"Target: \"{currentSegment.text}\"");
                Console.WriteLine($"Match:  \"{candidateText}\"\n");
                Console.ResetColor();

                var matchedWords = words.Skip(localStartPos).Take(currentBestLength).ToList();
                tempResult[currentSegment] = matchedWords;
                return tempResult;
            }

            string candidate1 = "", candidate2 = "";
            int nextScore1 = 0, nextScore2 = 0;
            // Lookahead for justification (requires 2 consecutive matches)
            for (int offset = 1; offset + 1 <= maxLookahead && (currentIndex + offset + 1) < segments.Count; offset++)
            {
                int estimatedPos1 = localStartPos + currentBestLength;
                var nextSegment1 = segments[currentIndex + offset];
                var nextSegment2 = segments[currentIndex + offset + 1];

                int NextBestLength1 = 0, NextBestLength2 = 0;

                // Try matching nextSegment1 at estimatedPos1
                for (int len = 1; estimatedPos1 + len <= words.Count; len++)
                {
                    var temp = string.Concat(words.Skip(estimatedPos1).Take(len).Select(w => w.Word));
                    int score = ScoreStringSimilarity(temp, nextSegment1.text);
                    if (score > nextScore1)
                    {
                        nextScore1 = score;
                        NextBestLength1 = len;
                        candidate1 = temp;
                    }
                    if (temp.Length > nextSegment1.text.Length + 10) break;
                }

                // Safety check: would nextSegment1 match even better at localStartPos?
                int conflictScore = 0;
                for (int len = 1; localStartPos + len <= words.Count; len++)
                {
                    var temp = string.Concat(words.Skip(localStartPos).Take(len).Select(w => w.Word));
                    int score = ScoreStringSimilarity(temp, nextSegment1.text);
                    if (score > conflictScore) conflictScore = score;
                    if (temp.Length > nextSegment1.text.Length + 10) break;
                }

                if (conflictScore >= nextScore1 && conflictScore >= scoreThreshold)
                {
                    // Overlap detected: nextSegment1 aligns better with current segment's location
                    Console.ForegroundColor = ConsoleColor.DarkYellow;
                    Console.WriteLine($"\n[FastMatch] ⚠ Rejecting lookahead for Segment {currentSegment.id},{currentIndex} due to overlap with Segment {nextSegment1.id}");
                    Console.WriteLine($"Conflict Score at localStartPos: {conflictScore} >= Score at estimatedPos1: {nextScore1}");
                    Console.ResetColor();
                    continue;
                }

                // Now check nextSegment2
                int estimatedPos2 = estimatedPos1 + NextBestLength1;
                for (int len = 1; estimatedPos2 + len <= words.Count; len++)
                {
                    var temp = string.Concat(words.Skip(estimatedPos2).Take(len).Select(w => w.Word));
                    int score = ScoreStringSimilarity(temp, nextSegment2.text);
                    if (score > nextScore2)
                    {
                        nextScore2 = score;
                        NextBestLength2 = len;
                        candidate2 = temp;
                    }
                    if (temp.Length > nextSegment2.text.Length + 10) break;
                }

                if (
                    (offset == 1 && nextScore1 >= scoreThreshold) ||
                    (offset == 1 && nextScore1 >= scoreThreshold - 10 && nextScore2 >= scoreThreshold - 10) ||
                    (offset > 1 && nextScore1 >= scoreThreshold && nextScore2 >= scoreThreshold)
                )
                {
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    Console.WriteLine($"\n[FastMatch] ⚠ Lookahead accepted Segment {currentSegment.id},{currentIndex} (rescued via Segments {nextSegment1.id}" +
                                      $"{(offset > 1 ? $" + {nextSegment2.id}" : "")})");
                    Console.ForegroundColor = ConsoleColor.Cyan;
                    Console.WriteLine($"Target: \"{currentSegment.text}\"");
                    Console.WriteLine($"Match:  \"{candidateText}\"\n");
                    Console.ResetColor();

                    var matchedWords = words.Skip(localStartPos).Take(currentBestLength).ToList();
                    tempResult[currentSegment] = matchedWords;
                    return tempResult;
                }
                else
                {
                    Console.WriteLine();
                }
            }



            // No match
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"\n[FastMatch] ❌ Skipped Segment {currentSegment.id},{currentIndex} | No confident match or lookahead fallback.");
            Console.ForegroundColor = ConsoleColor.DarkGray;
            Console.WriteLine($"Target: \"{currentSegment.text}\"");
            Console.WriteLine($"Closest Match Attempt: \"{candidateText}\" | Score: {currentBestScore}\n");
            Console.ResetColor();

            return null;
        }



        private static List<WordSegment> TrySlowMatch(
            List<Segment> segments,
            int segmentIndex,
            List<WordSegment> wordStream,
            ref int wordStartPos,
            int softDistanceLimit,
            int hardDistanceLimit,
            int scoreThreshold,
            int explorationDepth = 10)
        {
            Segment segment = segments[segmentIndex];
            int originalStart = wordStartPos;

            var match = FindMatchingWordSequence(wordStream, segment.text, ref wordStartPos, scoreThreshold, explorationDepth);

            if (match.Count == 0)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"\n[SlowMatch] ❌ Segment {segment.id} could not be matched.");
                Console.ResetColor();
                wordStartPos = originalStart;
                return null;
            }

            int matchPos = wordStream.IndexOf(match.First());
            int delta = matchPos - originalStart;

            // === Hard Limit Check ===
            if (delta > hardDistanceLimit)
            {
                Console.ForegroundColor = ConsoleColor.DarkRed;
                Console.WriteLine($"\n[SlowMatch] ⛔ Segment {segment.id} match exceeded hard limit (Δ = {delta}, limit = {hardDistanceLimit}). Skipping.");
                Console.ResetColor();

                wordStartPos = originalStart;
                return null;
            }

            // === Soft Limit Warning + Rescue Attempt ===
            if (originalStart != 0 && delta > softDistanceLimit)
            {
                Console.ForegroundColor = ConsoleColor.DarkYellow;
                Console.WriteLine($"\n[SlowMatch] ⚠ Segment {segment.id} matched too far ahead (Δ = {delta}, soft limit = {softDistanceLimit}).");
                Console.WriteLine($"Attempting fallback alignment via TryFastMatch at segment {segmentIndex + 1}...\n");
                Console.ResetColor();

                var rescue = TryFastMatch(segments, segmentIndex + 1, wordStream, matchPos + match.Count, 70, 2);

                if (rescue == null)
                {
                    Console.ForegroundColor = ConsoleColor.Red;
                    Console.WriteLine($"[SlowMatch] ❌ Rescue failed. Segment {segment.id} skipped.\n");
                    Console.ResetColor();

                    wordStartPos = originalStart;
                    return null;
                }
                else
                {
                    Console.ForegroundColor = ConsoleColor.Green;
                    Console.WriteLine($"[SlowMatch] ✅ Rescue succeeded. Segment {segment.id} accepted.");
                    Console.ForegroundColor = ConsoleColor.Cyan;
                    Console.WriteLine($"Target: \"{segment.text}\"");
                    Console.WriteLine($"Match:  \"{string.Join(" ", match.Select(w => w.Word))}\"\n");
                    Console.ResetColor();

                    wordStartPos = matchPos + match.Count;
                    return match;
                }
            }

            // === Accept Match Normally ===
            wordStartPos = matchPos + match.Count;
            return match;
        }



















        private static int[] RefineMatchLength(
    List<WordSegment> words,
    int bestScore,
    int bestStart,
    int initialLength,
    int exploration,
    string targetText)
        {
            int[] refinedMatch = new int[2] { bestStart, initialLength };

            for (int delta = -exploration; delta <= exploration; delta++)
            {
                int tryLength = initialLength + delta;
                if (tryLength <= 0 || bestStart + tryLength > words.Count)
                    continue;

                int score = ScoreMatch(words, bestStart, tryLength, targetText);
                if (score > bestScore)
                {
                    bestScore = score;
                    refinedMatch[1] = tryLength;
                }
            }

            return refinedMatch;
        }



    }
}
