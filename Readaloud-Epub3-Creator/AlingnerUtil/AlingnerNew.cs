using F23.StringSimilarity;
using FuzzySharp;
using System.Collections.Concurrent;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows.Documents.DocumentStructures;
using static Readaloud_Epub3_Creator.Alingner;
using static Readaloud_Epub3_Creator.EpubUtility;
using static Readaloud_Epub3_Creator.TranscriptClass;

namespace Readaloud_Epub3_Creator.AlingnerUtil
{
    public class AlingnerNew
    {
        public string WordPath { get; set; }

        public string LogPath { get; set; }

        public LogLevel MinLogLevel { get; set; } = LogLevel.Green;

        public List<WordSegment> BookSegments { get; set; }
        public List<Fragment> TranscriptSegments { get; set; }

        public byte[] words { get; set; }
        public ushort[] wordsTrigrams { get; set; }
        public AlingmentMapper[] WordsMap { get; set; }

        public byte[] fragments { get; set; }
        public ushort[] fragmentsTrigrams { get; set; }
        public AlingmentMapper[] FragmentsMap { get; set; }

        private const byte SpaceCode = 26;
        private const byte DotCode = 27;
        private const int AlphabetSize = 28;
        private const int Base2 = AlphabetSize * AlphabetSize;

        public AlingnerNew(ref List<WordSegment> bookSegments,
                           ref List<Fragment> transcriptSegments, string wordPath, string logPath)
        {
            WordPath = wordPath;
            LogPath = logPath;
            MinLogLevel = LogLevel.Green;
            BookSegments = bookSegments;
            TranscriptSegments = transcriptSegments;

            WordsMap = new AlingmentMapper[BookSegments.Count];
            words = BuildByteArray(BookSegments, WordsMap, s => s.NormalizedWord);

            FragmentsMap = new AlingmentMapper[TranscriptSegments.Count];
            fragments = BuildByteArray(TranscriptSegments, FragmentsMap, s => s.NormalizedText);

            wordsTrigrams = PrecalculateTrigrams(words);
            fragmentsTrigrams = PrecalculateTrigrams(fragments);

            UpdateMapperTrigramIndices(WordsMap);
            UpdateMapperTrigramIndices(FragmentsMap);
        }

        private static byte[] BuildByteArray<T>(
            List<T> segments,
            AlingmentMapper[] map,
            Func<T, string> selector)
            where T : class
        {
            // Precalculate total length
            int totalLength = 0;
            foreach (var seg in segments)
                totalLength += selector(seg).Length;

            byte[] result = new byte[totalLength];
            int pos = 0;

            foreach (dynamic segment in segments)
            {
                string text = selector(segment);

                map[segment.IndexInList] = new AlingmentMapper
                {
                    StartId = pos,
                    EndId = pos + text.Length,
                    ListIndex = segment.IndexInList
                };

                for (int i = 0; i < text.Length; i++)
                    result[pos++] = MapChar(text[i]);
            }

            return result;
        }

        private static ushort[] PrecalculateTrigrams(ReadOnlySpan<byte> text)
        {
            if (text.Length < 3)
                return Array.Empty<ushort>();

            ushort[] hashes = new ushort[text.Length - 2];
            Span<ushort> hashSpan = hashes;

            const int AlphabetSize = 27;
            const int Base2 = AlphabetSize * AlphabetSize; // 729

            for (int i = 0; i < text.Length - 2; i++)
            {
                hashSpan[i] = (ushort)(
                    text[i] * Base2 +
                    text[i + 1] * AlphabetSize +
                    text[i + 2]);
            }

            return hashes;
        }

        private static void UpdateMapperTrigramIndices(AlingmentMapper[] maps)
        {
            foreach (var m in maps)
            {
                m.TrigramStart = m.StartId;
                m.TrigramEnd = Math.Max(m.TrigramStart, m.EndId - 2);
            }
        }

        // Optional: Fast normalization without Regex
        public static string NormalizeText(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            Span<char> buffer = stackalloc char[text.Length];
            int pos = 0;
            bool lastWasSpace = true;

            foreach (char c in text)
            {
                char ch = c;

                if (ch >= 'A' && ch <= 'Z')
                    ch = (char)(ch + 32);

                if (ch >= 'a' && ch <= 'z')
                {
                    buffer[pos++] = ch;
                    lastWasSpace = false;
                }
                if (char.IsPunctuation(ch))
                {
                    buffer[pos++] = '.';
                    lastWasSpace = false;
                }
                else
                {
                    if (!lastWasSpace)
                    {
                        buffer[pos++] = ' ';
                        lastWasSpace = true;
                    }
                }
            }

            return new string(buffer[..(lastWasSpace && pos > 0 ? pos - 1 : pos)]);
        }
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static byte MapChar(char c)
        {
            if (c >= 'A' && c <= 'Z')
                c = (char)(c + 32);

            if (c >= 'a' && c <= 'z')
                return (byte)(c - 'a');

            if (c == ' ')
                return SpaceCode;

            if (c == '.')
                return DotCode;

            return SpaceCode;
        }

        public class AlingmentMapper
        {
            public int StartId;
            public int EndId;
            public int ListIndex;

            public int TrigramStart;
            public int TrigramEnd;
        }

        //´Main program


        public class AlignmentJob
        {
            // The range in the BookSegments list
            public int WordStartIndex { get; set; }
            public int WordEndIndex { get; set; }

            // The range in the TranscriptSegments list
            public int FragmentStartIndex { get; set; }
            public int FragmentEndIndex { get; set; }

            public int FragmentCount => FragmentEndIndex - FragmentStartIndex;
        }


        public void RunAlingment()
        {
            var rootJob = new AlignmentJob
            {
                WordStartIndex = 0,
                WordEndIndex = BookSegments.Count,
                FragmentStartIndex = 0,
                FragmentEndIndex = TranscriptSegments.Count
            };

            Stack<AlignmentJob> jobQueue = new Stack<AlignmentJob>();
            jobQueue.Push(rootJob);

            while (jobQueue.Count > 0)
            {
                var currentJob = jobQueue.Pop();

                // BASE CASE: If the job is small enough, use the high-precision algorithm
                if (currentJob.FragmentCount <= 50) // Adjust threshold as needed
                {
                    AlignMicroSegments(currentJob);
                    continue;
                }

                // STEP 1: Find a high-confidence anchor block (3 fragments)
                int sequenceSize = 3;
                int midFragIdx = currentJob.FragmentStartIndex + (currentJob.FragmentCount / 2);

                // Ensure we don't overflow the current job's fragments
                if (midFragIdx + sequenceSize > currentJob.FragmentEndIndex)
                    midFragIdx = currentJob.FragmentEndIndex - sequenceSize;

                AlignmentJob centerJob = FindAnchorSequenceInJob(currentJob, midFragIdx, sequenceSize);

                if (centerJob != null)
                {
                    // STEP 2: Push the three segments back onto the stack

                    // 1. RIGHT JOB (After the match)
                    jobQueue.Push(new AlignmentJob
                    {
                        WordStartIndex = centerJob.WordEndIndex,
                        WordEndIndex = currentJob.WordEndIndex,
                        FragmentStartIndex = centerJob.FragmentEndIndex,
                        FragmentEndIndex = currentJob.FragmentEndIndex
                    });

                    // 2. CENTER JOB (The confirmed match)
                    // It will be picked up next and processed by AlignMicroSegments 
                    // because its FragmentCount is exactly 'sequenceSize'
                    jobQueue.Push(centerJob);

                    // 3. LEFT JOB (Before the match)
                    jobQueue.Push(new AlignmentJob
                    {
                        WordStartIndex = currentJob.WordStartIndex,
                        WordEndIndex = centerJob.WordStartIndex,
                        FragmentStartIndex = currentJob.FragmentStartIndex,
                        FragmentEndIndex = centerJob.FragmentStartIndex
                    });
                }
                else
                {
                    // Fallback: If no anchor sequence found, try reducing search size 
                    // or mark this specific range for manual review/brute force.
                }
            }
        }

        private AlignmentJob FindAnchorSequenceInJob(AlignmentJob job, int startFragIdx, int fragCount)
        {
            // Get the combined trigram span for the fragments we are looking for
            var targetTrigrams = GetWordTrigrams(job.WordStartIndex,job.WordEndIndex);

            // Calculate the approximate character length of the fragment block
            var fragmentBytes = GetFragmentBytes(startFragIdx, startFragIdx + fragCount);
            int fragmentCharLength =fragmentBytes.Length;

            // Search the Book range
            int bestScore = 0;
            int bestWordStart = -1;
            int bestWordEnd = -1;

            // We slide through the word list, but we look at windows proportional to the fragment length
            // We can jump by steps (e.g., 2-3 words) to speed up the broad scan
            for (int i = job.WordStartIndex; i < job.WordEndIndex; i += 2)
            {
                // Estimate how many words to include in this window (roughly fragment length + 20% slack)
                int searchWindowChars = (int)(fragmentCharLength * 1.2);
                int endWordIdx = i;
                int currentChars = 0;

                while (endWordIdx < job.WordEndIndex && currentChars < searchWindowChars)
                {
                    currentChars += (WordsMap[endWordIdx].EndId - WordsMap[endWordIdx].StartId);
                    endWordIdx++;
                }

                // Get the trigram span for this window of words
                var windowTrigrams = GetWordTrigrams(i, endWordIdx-i);

                // 1. Broad Scan: Trigram Jaccard (Fast)
                int score = ScoreTrigramJaccard(targetTrigrams, windowTrigrams);

                if (score > bestScore)
                {
                    // 2. Verification: If score is high, do a more expensive ordered match
                    int orderedScore = ScoreOrderedTrigramMatch(targetTrigrams, windowTrigrams);

                    if (orderedScore > 80) // High confidence threshold
                    {
                        bestScore = orderedScore;
                        bestWordStart = i;
                        bestWordEnd = endWordIdx;
                    }
                }

                // Optimization: If we found a near-perfect match, exit early
                if (bestScore > 95) break;
            }

            if (bestWordStart == -1 || bestScore < 75) return null;

            return new AlignmentJob
            {
                WordStartIndex = bestWordStart,
                WordEndIndex = bestWordEnd,
                FragmentStartIndex = startFragIdx,
                FragmentEndIndex = startFragIdx + fragCount
            };
        }
        public void AlignMicroSegments(AlignmentJob job)
        {
            int wordIndex = job.WordStartIndex;
            for (int i = job.FragmentStartIndex; i < job.FragmentEndIndex; i++)
            {
                var result = ScoreSequentialSlotAccurate(
                    wordIndex,
                    i,
                    tolerance: 5,
                    minAcceptScore: 60);

                // --- BACKUP STRATEGY TRIGGER ---
                if (result.wordCount == 0 && TranscriptSegments[i].NormalizedText.Length > 0)
                {
                    var newjob = new AlignmentJob
                    {
                        WordStartIndex = wordIndex,
                        WordEndIndex = job.WordEndIndex,
                        FragmentStartIndex = i,
                        FragmentEndIndex = i + 1
                    };
                    // Try heavier fuzzy matching if the fast trigram match failed
                    var test = FindAnchorSequenceInJob(job, i, i + 1);
                    result = (test.WordEndIndex - wordIndex, 9999);
                    AplyMatch(i, wordIndex, result.wordCount);
                }

                if (result.wordCount == 0 && TranscriptSegments[i].NormalizedText.Length == 0)
                {
                    LogOutcome(i, LogLevel.Yellow, "Aligned empty fragment", wordIndex, FragmentsMap[i], 0);
                }
                else if (result.wordCount == 0)
                {
                    LogOutcome(i, LogLevel.Red, $"Total Failure at {i}", wordIndex, FragmentsMap[i], 0);
                    // Optionally: wordIndex++; // Skip one word to try and re-sync next loop
                }
                else
                {
                    LogOutcome(i, LogLevel.Green, $"Aligned (Score: {result.score}%)", wordIndex, FragmentsMap[i], result.wordCount);
                    AplyMatch(i, wordIndex, result.wordCount);
                    wordIndex += result.wordCount;
                }
            }
        }
        public void AplyMatch(int i, int wordIndex,int wordCount)
        {
            var MatchedFragment = TranscriptSegments[i];
            for (int j = wordIndex; j < wordCount + wordIndex; j++)
            {
                BookSegments[j].LinkedSegments.Add(MatchedFragment);
            }
            wordIndex += wordCount;
        }















        // Similarity scoring algorithms

        public int ScoreTrigramJaccard(
    ReadOnlySpan<ushort> a,
    ReadOnlySpan<ushort> b)
        {
            if (a.Length == 0 || b.Length == 0)
                return 0;

            Span<bool> used = stackalloc bool[b.Length];
            int intersection = 0;

            for (int i = 0; i < a.Length; i++)
            {
                for (int j = 0; j < b.Length; j++)
                {
                    if (!used[j] && a[i] == b[j])
                    {
                        used[j] = true;
                        intersection++;
                        break;
                    }
                }
            }

            int union = a.Length + b.Length - intersection;
            return (intersection * 100) / union;
        }


        public int ScoreOrderedTrigramMatch(
    ReadOnlySpan<ushort> a,
    ReadOnlySpan<ushort> b)
        {
            int maxMatch = 0;

            for (int i = 0; i < a.Length; i++)
            {
                int match = 0;

                for (int j = 0; j < b.Length && i + match < a.Length; j++)
                {
                    if (a[i + match] == b[j])
                        match++;
                    else if (match > 0)
                        break;
                }

                if (match > maxMatch)
                    maxMatch = match;
            }

            return (maxMatch * 100) / Math.Max(a.Length, b.Length);
        }

        public int ScoreEditDistance(
    ReadOnlySpan<byte> a,
    ReadOnlySpan<byte> b,
    int maxDistance = 50)
        {
            if (Math.Abs(a.Length - b.Length) > maxDistance)
                return 0;

            int[,] dp = new int[a.Length + 1, b.Length + 1];

            for (int i = 0; i <= a.Length; i++)
                dp[i, 0] = i;

            for (int j = 0; j <= b.Length; j++)
                dp[0, j] = j;

            for (int i = 1; i <= a.Length; i++)
            {
                int minRow = int.MaxValue;

                for (int j = 1; j <= b.Length; j++)
                {
                    int cost = a[i - 1] == b[j - 1] ? 0 : 1;

                    dp[i, j] = Math.Min(
                        Math.Min(dp[i - 1, j] + 1, dp[i, j - 1] + 1),
                        dp[i - 1, j - 1] + cost);

                    if (dp[i, j] < minRow)
                        minRow = dp[i, j];
                }

                if (minRow > maxDistance)
                    return 0; // early exit
            }

            int dist = dp[a.Length, b.Length];
            return 100 - (dist * 100 / Math.Max(a.Length, b.Length));
        }








        public (int start, int end, int score)
FindBestRegionByTrigramDensity(
    int wordIndex,
    int wordCount,
    int fragmentIndex,
    int fragmentCount,
    int windowSize = 10)
        {
            ReadOnlySpan<ushort> sourceTrigrams = fragmentsTrigrams.AsSpan(FragmentsMap[fragmentIndex].TrigramStart,FragmentsMap[fragmentIndex+ fragmentCount].TrigramEnd - FragmentsMap[fragmentIndex].TrigramStart);
            ReadOnlySpan<ushort> target = wordsTrigrams.AsSpan(WordsMap[wordIndex].TrigramStart, WordsMap[wordIndex+wordCount].TrigramEnd - WordsMap[wordIndex].TrigramStart);
            int bestScore = 0;
            int bestIndex = 0;

            for (int i = 0; i <= sourceTrigrams.Length - windowSize; i++)
            {
                var window = sourceTrigrams.Slice(i, windowSize);
                int score = ScoreTrigramJaccard(target, window);

                if (score > bestScore)
                {
                    bestScore = score;
                    bestIndex = i;
                }
            }

            return (bestIndex, bestIndex + windowSize, bestScore);
        }


        private static int FastOverlapDensity(
    ReadOnlySpan<ushort> a,
    ReadOnlySpan<ushort> b)
        {
            int min = Math.Min(a.Length, b.Length);
            int matches = 0;

            for (int i = 0; i < min; i++)
            {
                if (a[i] == b[i])
                    matches++;
            }

            return (matches * 100) / a.Length;
        }



        // Algorithm 


        public (int wordCount, int score)
ScoreSequentialSlotAccurate(
    int startWordIndex,
    int fragmentIndex,
    int tolerance = 3,
    int minAcceptScore = 70)
        {
            var fragMap = FragmentsMap[fragmentIndex];
            var fragTrigrams = GetFragmentTrigrams(fragmentIndex,fragmentIndex+1);

            if (fragTrigrams.Length == 0)
                return (0, 0);

            // ---- Phase 1: Estimate expected word count ----

            int fragmentCharLength = fragMap.EndId - fragMap.StartId;

            // estimate average chars per word from next 5 words
            int sampleWords = Math.Min(5, WordsMap.Length - startWordIndex);
            int sampleChars = 0;

            for (int i = 0; i < sampleWords; i++)
            {
                var m = WordsMap[startWordIndex + i];
                sampleChars += (m.EndId - m.StartId);
            }

            double avgCharsPerWord = sampleWords > 0
                ? (double)sampleChars / sampleWords
                : 5.0;

            int expectedWords = (int)Math.Round(fragmentCharLength / avgCharsPerWord);
            expectedWords = Math.Max(1, expectedWords);

            // ---- Phase 2: Refine locally ----

            int bestScore = 0;
            int bestWordCount = 0;

            int minWords = Math.Max(1, expectedWords - tolerance);
            int maxWords = Math.Min(
                WordsMap.Length - startWordIndex,
                expectedWords + tolerance);

            for (int w = minWords; w <= maxWords; w++)
            {
                var startMap = WordsMap[startWordIndex];
                var endMap = WordsMap[startWordIndex + w - 1];

                var window = wordsTrigrams.AsSpan(
                    startMap.TrigramStart,
                    endMap.TrigramEnd - startMap.TrigramStart);

                int score = FastOverlapDensity(fragTrigrams, window);

                // Bonus if ends on punctuation
                if (EndsOnDot(endMap))
                    score += 5;

                if (score > bestScore)
                {
                    bestScore = score;
                    bestWordCount = w;
                }
            }

            if (bestScore < minAcceptScore)
                return (0, bestScore);

            return (bestWordCount, bestScore);
        }



        //Helpers

        private bool EndsOnDot(AlingmentMapper map)
        {
            int charIndex = map.EndId - 1;
            return words[charIndex] == DotCode;
        }


        private ReadOnlySpan<byte> GetWordBytes(
    int startWord,
    int endWord)
        {
            if (startWord >= endWord)
                throw new ArgumentException("startWord must be less than endWord");

            int charStart = WordsMap[startWord].StartId;
            int charEnd = WordsMap[endWord - 1].EndId;

            return words.AsSpan(charStart,charEnd-charStart);
        }
        private ReadOnlySpan<ushort> GetWordTrigrams(
int startWord,
int endWord)
        {
            if (startWord >= endWord)
                throw new ArgumentException("startWord must be less than endWord");

            int charStart = WordsMap[startWord].TrigramStart;
            int charEnd = WordsMap[endWord - 1].TrigramEnd;

            return wordsTrigrams.AsSpan(charStart, charEnd - charStart);
        }

        private ReadOnlySpan<byte> GetFragmentBytes(
int startFragment,
int endFragment)
        {
            if (startFragment >= endFragment)
                throw new ArgumentException("startWord must be less than endWord");

            int charStart = FragmentsMap[startFragment].StartId;
            int charEnd = FragmentsMap[endFragment - 1].EndId;

            return fragments.AsSpan(charStart, charEnd - charStart);
        }
        private ReadOnlySpan<ushort> GetFragmentTrigrams(
int startFragment,
int endFragment)
        {
            if (startFragment >= endFragment)
                throw new ArgumentException("startWord must be less than endWord");

            int charStart = FragmentsMap[startFragment].TrigramStart;
            int charEnd = FragmentsMap[endFragment - 1].TrigramEnd;

            return fragmentsTrigrams.AsSpan(charStart, charEnd - charStart);
        }





        private static string DecodeRange(ReadOnlySpan<byte> bytes)
        {
            int length = bytes.Length;
            var chars = new char[length];

            for (int i = 0; i < length; i++)
            {
                byte b = bytes[i];

                if (b < 26)
                    chars[i] = (char)('a' + b);
                else if (b == 26)
                    chars[i] = ' ';
                else if (b == 27)
                    chars[i] = '.';
                else
                    chars[i] = '@';
            }

            return new string(chars);
        }










        //Logging

        private static readonly ConcurrentQueue<LogEntry> _logs
    = new ConcurrentQueue<LogEntry>();



        private void LogOutcome(
    int fragmentIndex,
    LogLevel level,
    string message,
    int wordPos,
    AlingmentMapper fragmentMap,
    int matchedWordCount = 0,
    bool isSystemMessage = false)
        {

            var wordsBuffer = words;
            var wordsMap = WordsMap;


            if (level < MinLogLevel)
                return;


            const int contextRadius = 50;

            int startWord = Math.Max(0, wordPos - contextRadius);
            int endWord = Math.Min(wordsMap.Length, wordPos + contextRadius);

            var snippetBytes = GetWordBytes(startWord, endWord);
            string snippet = DecodeRange(snippetBytes);

            var matchedBytes = GetWordBytes(wordPos, wordPos + matchedWordCount);
            string matchedText = DecodeRange(matchedBytes);

            var fragmentBytes = GetFragmentBytes(fragmentIndex, fragmentIndex);
            string targetText = DecodeRange(fragmentBytes);

            var entry = new LogEntry
            {
                FragmentIndex = fragmentIndex,
                StartPos = wordPos,
                Level = level,
                Message = message,
                ContextSnippet = snippet,
                MachedText = matchedText,
                TargetText = targetText,
                IsSystemMessage = isSystemMessage
            };

            _logs.Enqueue(entry);
        }


    }
}