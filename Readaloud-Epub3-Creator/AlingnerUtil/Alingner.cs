using F23.StringSimilarity;
using FuzzySharp;
using System.Collections.Concurrent;
using System.IO;
using System.Text;
using System.Text.Json;
using static Readaloud_Epub3_Creator.EpubUtility;
using static Readaloud_Epub3_Creator.TranscriptClass;

namespace Readaloud_Epub3_Creator
{
    public class AlingnerOld
    {
        public static F23.StringSimilarity.NGram l = new NGram(2);

        public static void AlignTranscriptToWords(ref List<WordSegment> words, List<Fragment> Fragments, string wordPath, int anchorCount = 30)
        {
            Dictionary<Fragment, List<WordSegment>> alignment = AlignFragmentsToWords(Fragments, words, anchorCount, Path.GetFullPath(Path.Combine(wordPath, @"..\")));

            foreach (var kvp in alignment)
            {
                var Fragment = kvp.Key;
                var matchedWords = kvp.Value;

                if (matchedWords.Count == 0)
                    continue;

                var sentenceGroups = SplitBySentence(matchedWords);
                foreach (var group in sentenceGroups)
                {
                    foreach (var word in group)
                    {
                        word.LinkedSegments.Add(Fragment);
                    }
                }
            }
            SaveWordSegments(words, wordPath);
        }
        public enum LogLevel { Green, Yellow, Red }

        public class LogEntry
        {
            public int FragmentIndex { get; set; }
            public int StartPos { get; set; }
            public LogLevel Level { get; set; }
            public string Message { get; set; }
            public string ContextSnippet { get; set; }

            public string MachedText { get; set; }
            public string TargetText { get; set; }

            public bool IsSystemMessage { get; set; } = false;
        }


        private static List<LogEntry> _logs;

        // Main alignment function using anchors with logging
        public static Dictionary<Fragment, List<WordSegment>> AlignFragmentsToWords(
            List<Fragment> Fragments,
            List<WordSegment> words,
            int anchorCount,
            string LogPath = "",
            int scoreThreshold = 65,
            int maxLookahead = 2
            )
        {
            _logs = new List<LogEntry>();
            var result = new Dictionary<Fragment, List<WordSegment>>();
            int n = Fragments.Count;

            // Choose anchors
            var anchors = new List<(int segIdx, int wordPos, List<WordSegment> match)>();
            int currentWordPos = 0;

            int maxSegIdx = Math.Max(0, Fragments.Count - 6);

            int failedAnchorsInARow = 0;

            for (int i = 0; i < anchorCount; i++)
            {
                int segIdx = i * maxSegIdx / (anchorCount - 1);

                // Clamp segIdx in case of rounding issues
                if (segIdx > maxSegIdx)
                    segIdx = maxSegIdx;

                int tempPos = currentWordPos;

                // Dynamically increase search window based on consecutive failures
                int baseWindow = words.Count / anchorCount;
                int extraWindow = failedAnchorsInARow * baseWindow;
                int searchWindow = (int)(baseWindow * 1.5) + extraWindow;

                var match = TrySlowMatch(
                    Fragments, ref segIdx, words, ref tempPos,
                    hardDistanceLimit: searchWindow,
                    isAnchor: true,
                    scoreThreshold: 70
                );

                bool hasMatch = match != null && match.Count > 0;
                bool regressed = tempPos < currentWordPos;

                LogLevel level;
                string message;

                if (!hasMatch)
                {
                    level = LogLevel.Red;
                    message = $"Anchor skipped: no match found (fail count = {failedAnchorsInARow + 1})";
                    failedAnchorsInARow++;
                }
                else if (regressed)
                {
                    level = LogLevel.Yellow;
                    message = $"Anchor regressed: tempPos={tempPos}, currentWordPos={currentWordPos}";
                    failedAnchorsInARow++;
                }
                else
                {
                    level = LogLevel.Green;
                    message = "Anchor matched successfully";
                    failedAnchorsInARow = 0;
                }

                LogOutcome(
                    segIdx,
                    level,
                    message,
                    tempPos,
                    words,
                    Fragments[segIdx].Text,
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

            // Align between anchors (multithreaded)
            var parallelResult = new ConcurrentDictionary<Fragment, List<WordSegment>>();

            Parallel.ForEach(
                Enumerable.Range(0, anchors.Count - 1),
        new ParallelOptions
        {
            MaxDegreeOfParallelism = Environment.ProcessorCount
        },
        a =>
        {
            var (startSeg, startWords, _) = anchors[a];
            var (endSeg, endWords, _) = anchors[a + 1];

            if (startSeg >= 0)
                parallelResult[Fragments[startSeg]] = anchors[a].match;

            int segCount = endSeg - startSeg - 1;
            if (segCount <= 0)
                return;

            var subFragments = Fragments.GetRange(startSeg + 1, segCount);
            var subWords = words.GetRange(startWords, endWords - startWords);
            int localPos = 0;

            for (int j = 0; j < subFragments.Count; j++)
            {
                var seg = subFragments[j];
                int globalIdx = startSeg + 1 + j;

                // --- FAST MATCH ---
                var fast = TryFastMatch(subFragments, j, subWords, localPos, scoreThreshold, maxLookahead);
                if (fast != null && fast.TryGetValue(seg, out var fMatch))
                {
                    parallelResult[seg] = fMatch;

                    LogOutcome(
                        globalIdx,
                        LogLevel.Green,
                        "FastMatch success",
                        localPos,
                        subWords,
                        seg.Text,
                        fMatch
                    );

                    localPos += fMatch.Count;
                    continue;
                }

                // --- SLOW MATCH ---
                int lookbackLimit = 100;
                int backwardPos = Math.Max(0, localPos - lookbackLimit);
                int tentativePos = backwardPos;

                var slow = TrySlowMatch(
                subFragments,
                ref j,
                subWords,
                ref tentativePos,
                hardDistanceLimit: 1000,
                scoreThreshold
            );

                var hasSlowMatch = slow != null && slow.Count > 0;
                var level = hasSlowMatch ? LogLevel.Yellow : LogLevel.Red;
                var msg = hasSlowMatch ? "SlowMatch fallback" : "Match skipped";

                if (hasSlowMatch)
                {
                    parallelResult[seg] = slow;
                    localPos = tentativePos;
                }

                LogOutcome(
                globalIdx,
                level,
                msg,
                tentativePos,
                subWords,
                seg.Text,
                slow
            );
            }
        }
    );

            // Merge results after parallel work
             result = parallelResult.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);


            // Save logs as JSON
            var options = new JsonSerializerOptions { WriteIndented = true };
            string json = JsonSerializer.Serialize(_logs, options);
            System.IO.File.WriteAllText(Path.Combine(LogPath, "AlingmentLog.json"), json);

            return result;
        }

        private static readonly object _logLock = new object();

        private static void LogOutcome(
            int FragmentIndex,
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

            var entry = new LogEntry
            {
                FragmentIndex = FragmentIndex,
                StartPos = wordPos,
                Level = level,
                Message = message,
                ContextSnippet = snippet,
                MachedText = matchedText,
                TargetText = targetText
            };
            //Console.WriteLine(FragmentIndex);
            //Console.WriteLine(level.ToString());

            // Thread-safe write
            lock (_logLock)
            {
                _logs.Add(entry);
            }
        }





        public static List<List<WordSegment>> SplitBySentence(List<WordSegment> words)
        {
            return words
                .GroupBy(w => w.ParentXPath)
                .Select(g => g.ToList())
                .ToList();
        }

        private static Dictionary<Fragment, List<WordSegment>> TryFastMatch(
         List<Fragment> Fragments,
         int currentIndex,
         List<WordSegment> words,
         int startPos,
         int scoreThreshold,
         int maxLookahead)
        {
            var results = new Dictionary<Fragment, List<WordSegment>>();
            if (currentIndex == Fragments.Count)
            {
                currentIndex--;
            }
            var current = Fragments[currentIndex];

            // 1. Find best single match for current Fragment
            var (bestScore, bestLen) = FindBestMatchAt(words, startPos, current.Text);
            if (bestScore >= scoreThreshold && bestLen > 0)
            {
                results[current] = words.Skip(startPos).Take(bestLen).ToList();
                return results;
            }

            // 2. Attempt lookahead justification
            if (TryLookahead(
                Fragments, currentIndex, words, startPos,
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
            string targetText,
            int minCommonBigrams = 10)
        {
            var results = new List<(int score, int length, int diff)>();
            string normTarget = Normalize(targetText);
            var targetBigrams = GetCharacterBigrams(normTarget);
            if (targetBigrams.Count() < minCommonBigrams) {
                minCommonBigrams = targetBigrams.Count() / 2;
            }
            StringBuilder sb = new StringBuilder();

            int consecutiveIncrease = 0;
            int prevDiff = -1;

            for (int len = 1; pos + len <= words.Count; len++)
            {
                sb.Append(words[pos + len - 1].Word);
                string candidate = Normalize(sb.ToString());

                int diff = Math.Abs(candidate.Length - normTarget.Length);

                // Track consecutive increases
                if (prevDiff != -1 && diff > prevDiff)
                    consecutiveIncrease++;
                else
                    consecutiveIncrease = 0;

                if (consecutiveIncrease >= 3)
                    break;

                prevDiff = diff;

                int score = (int)(l.Distance(candidate, normTarget) * 100);
                results.Add((score, len, diff));
            }

            // Order by smallest difference first
            var topResults = results.OrderBy(r => r.diff)
                                    .ThenByDescending(r => r.score) // optional tie-breaker by score
                                    .Take(2)
                                    .ToList();
            int bestScore = 0;
            int bestLen = 0;
            foreach (var res in topResults)
            {
                string candidate = Normalize(ConcatWords(words, pos, res.length));


                // FILTER 2: n‑gram overlap
                var candBigrams = GetCharacterBigrams(candidate);
                int common = candBigrams.Intersect(targetBigrams).Count();
                if (common < minCommonBigrams) continue;

                // Now expensive full fuzz ratio
                int score = Fuzz.Ratio(candidate, normTarget);
                if (score > bestScore)
                {
                    bestScore = score;
                    bestLen = res.length;
                    if (bestScore == 100) break;  // perfect match early exit
                }
            }

            return (bestScore, bestLen);
        }


        public static IEnumerable<string> GetCharacterBigrams(string s)
        {
            if (String.IsNullOrEmpty(s) || s.Length < 2)
                yield break;
            for (int i = 0; i < s.Length - 1; i++)
            {
                yield return s.Substring(i, 2);
            }
        }


        private static bool TryLookahead(
            List<Fragment> Fragments,
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
                 offset <= maxLookahead && currentIndex + offset + 1 < Fragments.Count;
                 offset++)
            {
                int pos1 = startPos + currentBestLen;
                var seg1 = Fragments[currentIndex + offset];
                var seg2 = Fragments[currentIndex + offset + 1];

                // 2a. Best match for next Fragment at pos1
                var (score1, len1) = FindBestMatchAt(words, pos1, seg1.Text);

                // 2b. Conflict check: can seg1 match as well or better at startPos?
                var (conflictScore, _) = FindBestMatchAt(words, startPos, seg1.Text);
                if (conflictScore >= score1 && conflictScore >= scoreThreshold)
                    continue;

                // 2c. Now match the following Fragment
                var (score2, len2) = FindBestMatchAt(words, pos1 + len1, seg2.Text);

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
            List<Fragment> Fragments,
            ref int FragmentIndex,
            List<WordSegment> wordStream,
            ref int wordStartPos,
            int hardDistanceLimit,
            int scoreThreshold,
            bool isAnchor = false)
        {
            var Fragment = Fragments[FragmentIndex];
            int originalStart = wordStartPos;
            int searchLimit = Math.Min(wordStream.Count, originalStart + hardDistanceLimit);

            // 🔍 Use a longer target text for initial approximate positioning
            string targetText = BuildTargetText(Fragments, FragmentIndex, minLength: 150);
            (int start, int end, int score)? region;

            if (hardDistanceLimit > 1000)
            {
                region = SelectPromisingRegionIndices(wordStream, targetText, originalStart, searchLimit);
                if (region == null)
                    return null;
            }
            else
            {
                region = (wordStartPos, wordStartPos + hardDistanceLimit, 100);
            }

            var (regionStart, regionEnd, regionScore) = region.Value;

            if (regionScore < 40) // fallback: region too weak
                return null;

            // 🎯 Focus scanning within the promising region
            var (bestPos, bestLen, bestScore) = FocusScanRegion(wordStream, regionStart, regionEnd, Fragment, scoreThreshold);

            // 🧠 Anchor refinement for the next Fragment
            if (isAnchor)
            {
                int nextFragmentIndex = FragmentIndex + 1;
                if (nextFragmentIndex < Fragments.Count)
                {
                    var (nextPos, nextLen, nextScore) = FocusScanRegion(wordStream, bestPos, regionEnd, Fragments[nextFragmentIndex], scoreThreshold);
                    if (nextScore > bestScore + 5)
                    {
                        bestPos = nextPos;
                        FragmentIndex = nextFragmentIndex;
                        bestScore = nextScore;
                        bestLen = nextLen;
                    }
                }
            }
            else
            {
                int nextFragmentIndex = FragmentIndex + 1;
                if (nextFragmentIndex < Fragments.Count)
                {
                    var (nextPos, nextLen, nextScore) = FocusScanRegion(wordStream, bestPos, regionEnd, Fragments[nextFragmentIndex], scoreThreshold);
                    if (nextScore > bestScore + 5)
                    {
                        bestScore = nextScore;
                        bestLen = nextPos - bestPos;
                    }
                }
            }

            // ✅ Final check: only return if score is high enough
            if (bestScore < scoreThreshold || bestLen == 0)
                return null;

            wordStartPos = bestPos + bestLen;
            return wordStream.Skip(bestPos).Take(bestLen).ToList();
        }

        // 🎯 Focus scanning within the promising region
        private static (int bestPos, int bestLen, int bestScore) FocusScanRegion(
            List<WordSegment> wordStream,
            int regionStart,
            int regionEnd,
            Fragment Fragment,
            int scoreThreshold)
        {
            int bestScore = 0;
            int bestLen = 0;
            int bestPos = regionStart;
            int stagnantCycles = 0;

            for (int pos = regionStart; pos < regionEnd; pos++)
            {
                var (score, len) = FindBestMatchAt(wordStream, pos, Fragment.Text);

                if (score > bestScore)
                {
                    bestScore = score;
                    bestLen = len;
                    bestPos = pos;
                    stagnantCycles = 0;

                    if (score >= 99) // perfect match
                        break;
                }
                else
                {
                    stagnantCycles++;
                    if (bestScore >= scoreThreshold && stagnantCycles >= 10)
                        break;
                }
            }

            return (bestPos, bestLen, bestScore);
        }





        private static string BuildTargetText(List<Fragment> Fragments, int startIndex, int minLength)
        {
            string text = Fragments[startIndex].Text;
            int next = startIndex + 1;

            while (text.Length < minLength && next < Fragments.Count)
            {
                text += Fragments[next].Text;
                next++;
            }

            return text;
        }

        private static (int start, int end, int score)? SelectPromisingRegionIndices(
            List<WordSegment> wordStream,
            string targetText,
            int startIndex,
            int endIndex,
            int threshold = 85,
            int depth = 10,
            int minSize = 150)
        {
            if (depth == 0 || endIndex - startIndex < minSize)
            {
                string text = ConcatWords(wordStream, startIndex, endIndex - startIndex);
                int score = Fuzz.WeightedRatio(targetText, text);
                return (startIndex, endIndex, score);
            }

            int mid = (startIndex + endIndex) / 2;
            int q1 = startIndex + (endIndex - startIndex) / 4;
            int q3 = startIndex + 3 * (endIndex - startIndex) / 4;

            var regions = new (int start, int end)[]
            {
        (startIndex, mid),
        (q1, q3),
        (mid, endIndex)
            };

            int bestScore = 0;
            int bestStart = startIndex, bestEnd = endIndex;

            foreach (var (s, e) in regions)
            {
                string text = ConcatWords(wordStream, s, e - s);
                int score = Fuzz.WeightedRatio(targetText, text);

                if (score > bestScore)
                {
                    bestScore = score;
                    bestStart = s;
                    bestEnd = e;
                }
            }

            //if (bestScore < threshold - 10)
            //    return (bestStart, bestEnd, bestScore);
            if (startIndex == bestStart && endIndex == bestEnd)
            {
                return (bestStart, bestEnd, bestScore);
            }
            // Recursive refinement
            var deeper = SelectPromisingRegionIndices(wordStream, targetText, bestStart, bestEnd, threshold, depth - 1, minSize);
            if (deeper.HasValue && deeper.Value.score > bestScore)
                return deeper;

            return (bestStart, bestEnd, bestScore);
        }


        private static (string region, int score) QuickRegionSearch(
            string target,
            string text,
            int threshold,
            int depth,
            int minSize)
        {
            if (depth == 0 || text.Length < minSize)
                return (text, Fuzz.PartialRatio(target, text));

            int mid = text.Length / 2;
            int q1 = text.Length / 4;
            int q3 = (3 * text.Length) / 4;

            var regions = new[]
            {
        text[..mid],
        text[q1..q3],
        text[mid..]
    };

            int bestScore = 0;
            string bestRegion = text;

            foreach (var region in regions)
            {
                int score = Fuzz.PartialRatio(target, region);
                if (score > bestScore)
                {
                    bestScore = score;
                    bestRegion = region;
                }
            }

            // Stop early if all scores are low
            if (bestScore < threshold - 10)
                return (bestRegion, bestScore);

            // Recurse deeper into best region
            var (deeperRegion, deeperScore) = QuickRegionSearch(target, bestRegion, threshold, depth - 1, minSize);
            return deeperScore > bestScore ? (deeperRegion, deeperScore) : (bestRegion, bestScore);
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
