using FuzzySharp;
using System.IO;
using System.Text;
using System.Text.Json;
using static Readaloud_Epub3_Creator.EpubUtility;
using static Readaloud_Epub3_Creator.TranscriptClass;

namespace Readaloud_Epub3_Creator
{
    internal class Alingner
    {


        public static void AlignTranscriptToWords(ref List<WordSegment> words, List<Segment> segments, string wordPath, int anchorCount = 30)
        {
            Dictionary<Segment, List<WordSegment>> alignment = AlignSegmentsToWords(segments, words, anchorCount, Path.GetFullPath(Path.Combine(wordPath, @"..\")));

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
        public enum LogLevel { Green, Yellow, Red }

        public class LogEntry
        {
            public int SegmentIndex { get; set; }
            public int StartPos { get; set; }
            public LogLevel Level { get; set; }
            public string Message { get; set; }
            public string ContextSnippet { get; set; }

            public string MachedText { get; set; }
            public string TargetText { get; set; }

            public bool IsSystemMessage { get; set; } = false; // <- NEW
        }


        private static List<LogEntry> _logs;

        // Main alignment function using anchors with logging
        public static Dictionary<Segment, List<WordSegment>> AlignSegmentsToWords(
            List<Segment> segments,
            List<WordSegment> words,
            int anchorCount,
            string LogPath = "",
            int scoreThreshold = 65,
            int maxLookahead = 2
            )
        {
            _logs = new List<LogEntry>();
            var result = new Dictionary<Segment, List<WordSegment>>();
            int n = segments.Count;

            // Choose anchors
            var anchors = new List<(int segIdx, int wordPos, List<WordSegment> match)>();
            int currentWordPos = 0;

            int maxSegIdx = Math.Max(0, segments.Count - 6);

            for (int i = 0; i < anchorCount; i++)
            {
                int segIdx = i * maxSegIdx / (anchorCount - 1);

                // Clamp segIdx in case of rounding issues
                if (segIdx > maxSegIdx)
                    segIdx = maxSegIdx;

                int tempPos = currentWordPos;

                var match = TrySlowMatch(
                    segments, segIdx, words, ref tempPos,
                    hardDistanceLimit: (int)((words.Count/ anchorCount)*1.5),
                    scoreThreshold: 70
                );

                bool hasMatch = match != null && match.Count > 0;
                bool regressed = tempPos < currentWordPos;

                LogLevel level;
                string message;

                if (!hasMatch)
                {
                    level = LogLevel.Red;
                    message = "Anchor skipped: no match found";
                }
                else if (regressed)
                {
                    level = LogLevel.Yellow;
                    message = $"Anchor regressed: tempPos={tempPos}, currentWordPos={currentWordPos}";
                }
                else
                {
                    level = LogLevel.Green;
                    message = "Anchor matched successfully";
                }

                LogOutcome(
                    segIdx,
                    level,
                    message,
                    tempPos,
                    words,
                    segments[segIdx].text,
                    match
                );

                if (hasMatch && !regressed)
                {
                    anchors.Add((segIdx, tempPos, match));
                    currentWordPos = tempPos;
                }
            }






            anchors = anchors.OrderBy(a => a.segIdx).ToList();

            anchors.Insert(0, (-1, 0, new List<WordSegment>()));
            anchors.Add((n, words.Count, new List<WordSegment>()));


            // Align between anchors
            for (int a = 0; a < anchors.Count - 1; a++)
            {
                var (startSeg, startWords, _) = anchors[a];
                var (endSeg, endWords, _) = anchors[a + 1];

                if (startSeg >= 0)
                    result[segments[startSeg]] = anchors[a].match;

                int segCount = endSeg - startSeg - 1;
                if (segCount <= 0) continue;

                var subSegments = segments.GetRange(startSeg + 1, segCount);
                var subWords = words.GetRange(startWords, endWords - startWords);
                int localPos = 0;

                for (int j = 0; j < subSegments.Count; j++)
                {
                    var seg = subSegments[j];
                    int globalIdx = startSeg + 1 + j;

                    // Fast match
                    var fast = TryFastMatch(subSegments, j, subWords, localPos, scoreThreshold, maxLookahead);
                    if (fast != null && fast.TryGetValue(seg, out var fMatch))
                    {
                        result[seg] = fMatch;

                        LogOutcome(
                            globalIdx,
                            LogLevel.Green,
                            "FastMatch success",
                            localPos,
                            subWords,
                            seg.text,
                            fMatch
                        );

                        localPos += fMatch.Count;
                        continue;
                    }

                    // Slow match
                    int posRef = localPos;
                    var slow = TrySlowMatch(
                        subSegments,
                        j,
                        subWords,
                        ref posRef,
                        hardDistanceLimit: 100,
                        scoreThreshold
                    );

                    result[seg] = slow ?? new List<WordSegment>();

                    var hasSlowMatch = slow != null && slow.Count > 0;
                    var level = hasSlowMatch ? LogLevel.Yellow : LogLevel.Red;
                    var msg = hasSlowMatch ? "SlowMatch fallback" : "Match skipped";

                    LogOutcome(
                        globalIdx,
                        level,
                        msg,
                        posRef,
                        subWords,
                        seg.text,
                        slow
                    );


                    localPos = posRef;
                }
            }

            // Save logs as JSON
            var options = new JsonSerializerOptions { WriteIndented = true };
            string json = JsonSerializer.Serialize(_logs, options);
            System.IO.File.WriteAllText(Path.Combine(LogPath, "AlingmentLog.json"), json);

            return result;
        }

        private static void LogOutcome(
            int segmentIndex,
            LogLevel level,
            string message,
            int wordPos,
            List<WordSegment> words,
            string targetText,
            List<WordSegment> matchedWords = null)
        {
            int start = Math.Max(0, wordPos - 50);
            int count = Math.Min(100, words.Count - start);
            string snippet = string.Concat(words.Skip(start).Take(count).Select(w => w.Word));

            string matchedText = matchedWords != null
                ? string.Concat(matchedWords.Select(w => w.Word))
                : string.Empty;

            _logs.Add(new LogEntry
            {
                SegmentIndex = segmentIndex,
                StartPos = wordPos,
                Level = level,
                Message = message,
                ContextSnippet = snippet,
                MachedText = matchedText,
                TargetText = targetText
            });
        }




        public static List<List<WordSegment>> SplitBySentence(List<WordSegment> words)
        {
            return words
                .GroupBy(w => w.ParentXPath)
                .Select(g => g.ToList())
                .ToList();
        }

        private static Dictionary<Segment, List<WordSegment>> TryFastMatch(
         List<Segment> segments,
         int currentIndex,
         List<WordSegment> words,
         int startPos,
         int scoreThreshold,
         int maxLookahead)
        {
            var results = new Dictionary<Segment, List<WordSegment>>();
            if (currentIndex == segments.Count)
            {
                currentIndex--;
            }
            var current = segments[currentIndex];

            // 1. Find best single match for current segment
            var (bestScore, bestLen) = FindBestMatchAt(words, startPos, current.text);
            if (bestScore >= scoreThreshold && bestLen > 0)
            {
                results[current] = words.Skip(startPos).Take(bestLen).ToList();
                return results;
            }

            // 2. Attempt lookahead justification
            if (TryLookahead(
                segments, currentIndex, words, startPos,
                scoreThreshold, maxLookahead, bestLen,
                out var lookaheadLen))
            {
                results[current] = words.Skip(startPos).Take(lookaheadLen).ToList();
                return results;
            }

            // 3. No acceptable match
            return null;
        }

        private static (int score, int length) FindBestMatchAt(
            List<WordSegment> words,
            int pos,
            string targetText)
        {
            int bestScore = 0, bestLen = 0;
            for (int len = 1; pos + len <= words.Count; len++)
            {
                string candidate = ConcatWords(words, pos, len);
                int score = ScoreStringSimilarity(candidate, targetText);

                if (score > bestScore)
                {
                    bestScore = score;
                    bestLen = len;
                }
                if (candidate.Length > targetText.Length + 10)
                    break;
            }
            return (bestScore, bestLen);
        }

        private static bool TryLookahead(
            List<Segment> segments,
            int currentIndex,
            List<WordSegment> words,
            int startPos,
            int scoreThreshold,
            int maxLookahead,
            int currentBestLen,
            out int acceptedLength)
        {
            acceptedLength = currentBestLen;

            for (int offset = 1;
                 offset <= maxLookahead && currentIndex + offset + 1 < segments.Count;
                 offset++)
            {
                int pos1 = startPos + currentBestLen;
                var seg1 = segments[currentIndex + offset];
                var seg2 = segments[currentIndex + offset + 1];

                // 2a. Best match for next segment at pos1
                var (score1, len1) = FindBestMatchAt(words, pos1, seg1.text);

                // 2b. Conflict check: can seg1 match as well or better at startPos?
                var (conflictScore, _) = FindBestMatchAt(words, startPos, seg1.text);
                if (conflictScore >= score1 && conflictScore >= scoreThreshold)
                    continue;

                // 2c. Now match the following segment
                var (score2, len2) = FindBestMatchAt(words, pos1 + len1, seg2.text);

                // 2d. Apply your offset rules
                bool ok = offset == 1
                    ? (score1 >= scoreThreshold ||
                       (score1 >= scoreThreshold - 10 && score2 >= scoreThreshold - 10))
                    : (score1 >= scoreThreshold && score2 >= scoreThreshold);

                if (ok)
                {
                    acceptedLength = currentBestLen;
                    return true;
                }
            }

            return false;
        }

        private static string ConcatWords(
            List<WordSegment> words, int start, int count) =>
            string.Concat(words.Skip(start).Take(count).Select(w => w.Word));


        // === MAIN ENTRY ===
        private static List<WordSegment> TrySlowMatch(
            List<Segment> segments,
            int segmentIndex,
            List<WordSegment> wordStream,
            ref int wordStartPos,
            int hardDistanceLimit,
            int scoreThreshold,
            int batchSize = 600)
        {
            var segment = segments[segmentIndex];
            int originalStart = wordStartPos;
            int searchLimit = Math.Min(wordStream.Count, originalStart + hardDistanceLimit);

            int bestScore = 0;
            int bestLen = 0;
            int bestPos = originalStart;

            int stagnantCycles = 0;

            for (int batchStart = originalStart; batchStart < searchLimit; batchStart += batchSize/2)
            {
                int lengthThreshold = 150;
                string targetText = segment.text;
                int nextIndex = segmentIndex + 1;

                while (targetText.Length < lengthThreshold && nextIndex < segments.Count)
                {
                    targetText += segments[nextIndex].text;
                    nextIndex++;
                }

                string x = ConcatWords(wordStream, batchStart, batchSize);
                var f = Fuzz.PartialRatio(targetText, x);
                if (f > 55)
                {
                    bool improved = false;

                    for (int i = 0; i < batchSize && batchStart + i < searchLimit; i++)
                    {
                        int pos = batchStart + i;
                        var (score, len) = FindBestMatchAt(wordStream, pos, targetText);
                        if (score > bestScore)
                        {
                            bestScore = score;
                            bestLen = len;
                            bestPos = pos;
                            improved = true;

                            if (score >= 99)
                            {
                                (bestScore, bestLen) = FindBestMatchAt(wordStream, bestPos, segment.text);
                                wordStartPos = bestPos + bestLen;
                                return wordStream.Skip(bestPos).Take(bestLen).ToList(); // Early exit
                            }
                        }
                    }

                    // Track stagnation
                    if (!improved)
                    {
                        stagnantCycles++;

                        // Early exit condition
                        if (bestScore >= 80 && stagnantCycles >= 10)
                        {
                            Console.WriteLine("Early exit due to stagnation.");
                            (bestScore, bestLen) = FindBestMatchAt(wordStream, bestPos, segment.text);
                            wordStartPos = bestPos + bestLen;
                            return wordStream.Skip(bestPos).Take(bestLen).ToList();
                        }
                    }
                    else
                    {
                        stagnantCycles = 0;
                    }
                }


                // If best score is acceptable-ish, stop scanning and attempt fallback
                if (bestScore >= scoreThreshold - 10)
                {
                    (bestScore, bestLen) = FindBestMatchAt(wordStream, bestPos, segment.text);
                    var rescue = TryFastMatch(
                        segments,
                        segmentIndex + 1,
                        wordStream,
                        bestPos + bestLen,
                        scoreThreshold,
                        maxLookahead: 2);

                    if (rescue == null)
                    {
                        return null;
                    }
                    else
                    {
                        bestScore = bestScore + 15;
                        continue;
                    }


                }
            }

            // Hard reject
            if (bestScore < scoreThreshold || bestLen == 0)
            {
                return null;
            }
            (bestScore, bestLen) = FindBestMatchAt(wordStream, bestPos, segment.text);
            wordStartPos = bestPos + bestLen;
            return wordStream.Skip(bestPos).Take(bestLen).ToList();
        }








        private static string Normalize(string s)
        {
            var sb = new StringBuilder(s.Length);
            foreach (char c in s)
            {
                if (char.IsLetter(c) || char.IsWhiteSpace(c))
                {
                    sb.Append(char.ToLowerInvariant(c));
                }
                else if (char.IsPunctuation(c))
                {
                    sb.Append('.');
                }
            }
            return sb.ToString();
        }

        private static int ScoreStringSimilarity(string a, string b)
        {
            return Fuzz.Ratio(Normalize(a), Normalize(b));
        }




    }
}
