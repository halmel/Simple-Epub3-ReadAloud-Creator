using FuzzySharp;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
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

        // Core arrays: now char[] instead of byte[]
        // The debugger will now ignore this property in the Watch/Locals windows
        public char[] words { get; }
        public AlingmentMapper[] WordsMap { get; set; }
        public AlingmentMapper[] WordsSentances { get; set; }
        // The debugger will now ignore this property in the Watch/Locals windows
        public char[] fragments { get; }
        public AlingmentMapper[] FragmentsMap { get; set; }

        public AlingnerNew(ref List<WordSegment> bookSegments,
                           ref List<Fragment> transcriptSegments,
                           string wordPath, string logPath)
        {
            WordPath = wordPath;
            LogPath = logPath;
            MinLogLevel = LogLevel.Green;
            BookSegments = bookSegments;
            TranscriptSegments = transcriptSegments;

            WordsMap = new AlingmentMapper[BookSegments.Count];
            for (int i = 0; i < BookSegments.Count; i++)
                WordsMap[i] = new AlingmentMapper();

            words = BuildCharArray(BookSegments, WordsMap, s => s.NormalizedWord);

            FragmentsMap = new AlingmentMapper[TranscriptSegments.Count];
            for (int i = 0; i < TranscriptSegments.Count; i++)
                FragmentsMap[i] = new AlingmentMapper();

            fragments = BuildCharArray(TranscriptSegments, FragmentsMap, s => s.NormalizedText);

            WordsSentances = BuildSentenceMapFromWords();
        }

        public class AlingmentMapper
        {
            public int StartId;
            public int EndId;
            public int ListIndex;

            public int Length
            {
                get
                {
                    return EndId - StartId;
                }
            }

            public int FirstWordIndex;//sentance only
            public int LastWordIndex;//sentance only
        }

        // ------------------------------------------------------------
        // Build a flat char[] from segments, inserting spaces between
        // words when needed (same logic as the original byte version).
        // ------------------------------------------------------------
        private static char[] BuildCharArray<T>(
            List<T> segments,
            AlingmentMapper[] map,
            Func<T, string> selector) where T : class
        {
            var result = new List<char>();
            int pos = 0;
            bool lastWasSpace = true;   // start of text → no leading space

            foreach (var segment in segments)
            {
                string text = selector(segment);            // already normalized
                int segIndex = (segment as dynamic).IndexInList;

                if (string.IsNullOrEmpty(text))
                {
                    // Empty segment: still record its (zero-length) position
                    map[segIndex] = new AlingmentMapper
                    {
                        StartId = pos,
                        EndId = pos,
                        ListIndex = segIndex
                    };
                    continue;
                }

                // Should we insert a space before this segment?
                if (!lastWasSpace && text[0] != '.')
                {
                    result.Add(' ');
                    pos++;
                }

                // Record the start position for this segment
                int startPos = pos;

                // Append all characters of the normalized text
                foreach (char ch in text)
                    result.Add(ch);

                pos += text.Length;

                // Record the end position
                map[segIndex] = new AlingmentMapper
                {
                    StartId = startPos,
                    EndId = pos,
                    ListIndex = segIndex
                };

                // Normalized text never ends with a space, so lastWasSpace becomes false
                lastWasSpace = false;
            }

            return result.ToArray();
        }

        // ------------------------------------------------------------
        // Normalisation: letters → lowercase, non‑letters become spaces
        // (except '.' which is kept), multiple spaces collapsed.
        // ------------------------------------------------------------
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
                else if (ch == '.' || ch == ',')
                {
                    buffer[pos++] = '.';
                    lastWasSpace = false;
                }
                else if (!lastWasSpace)
                {
                    buffer[pos++] = ' ';
                    lastWasSpace = true;
                }
            }

            // Trim trailing space
            return new string(buffer[..(lastWasSpace && pos > 0 ? pos - 1 : pos)]);
        }

        // ------------------------------------------------------------
        // Sentence detection – uses '.' directly instead of a byte code.
        // ------------------------------------------------------------
        private AlingmentMapper[] BuildSentenceMapFromWords()
        {
            var sentences = new List<AlingmentMapper>();

            int? sentenceStartChar = null;
            int sentenceStartWord = 0;
            int sentenceIndex = 0;

            for (int i = 0; i < WordsMap.Length; i++)
            {
                var word = WordsMap[i];

                if (sentenceStartChar == null)
                {
                    sentenceStartChar = word.StartId;
                    sentenceStartWord = i;
                }

                // Does this word end with a period?
                bool isEnd = word.EndId > word.StartId &&
                             words[word.EndId - 1] == '.';

                if (isEnd)
                {
                    sentences.Add(new AlingmentMapper
                    {
                        StartId = sentenceStartChar.Value,
                        EndId = word.EndId,
                        ListIndex = sentenceIndex++,
                        FirstWordIndex = sentenceStartWord,
                        LastWordIndex = i
                    });

                    sentenceStartChar = null;
                }
            }

            return sentences.ToArray();
        }

        // ------------------------------------------------------------
        // Helper spans over the flat char arrays.
        // ------------------------------------------------------------
        private ReadOnlySpan<char> GetWordChars(int firstWordInclusive, int lastWordInclusive)
        {
            lastWordInclusive = Math.Min(lastWordInclusive, WordsMap.Length - 1);
            if (firstWordInclusive > lastWordInclusive)
                throw new ArgumentException("firstWordInclusive must be <= lastWordInclusive");

            var firstWord = WordsMap[firstWordInclusive];
            var lastWord = WordsMap[lastWordInclusive];
            return words.AsSpan(firstWord.StartId, lastWord.EndId - firstWord.StartId);
        }

        private ReadOnlySpan<char> GetFragmentChars(int firstFragmentInclusive, int lastFragmentInclusive)
        {
            if (firstFragmentInclusive > lastFragmentInclusive)
                throw new ArgumentException("firstFragmentInclusive must be <= lastFragmentInclusive");

            var firstFragment = FragmentsMap[firstFragmentInclusive];
            var lastFragment = FragmentsMap[lastFragmentInclusive];
            return fragments.AsSpan(firstFragment.StartId, lastFragment.EndId - firstFragment.StartId);
        }
        /// <summary>
        /// Returns how many items (starting from startIndex) are needed to reach or exceed the targetCharLength.
        /// </summary>
        private int GetCountToReachLength(AlingmentMapper[] map, int startIndex, int targetCharLength)
        {
            // Ensure we have at least one element to look at, and we aren't at the very last index
            if (map == null || startIndex < 0 || startIndex >= map.Length - 1)
                return 0;

            int accumulatedLength = 0;
            int count = 0;

            // We stop at map.Length - 1 to ensure (startIndex + count) is always a valid index
            for (int i = startIndex; i < map.Length - 1; i++)
            {
                accumulatedLength += map[i].Length;
                count++;

                if (accumulatedLength >= targetCharLength)
                    break;
            }

            return count;
        }

        // Public Wrappers for the three ID types:
        private const int DEFAULT_LENGHT = 50;
        public int GetFragmentCountForLength(int startFragmentId, int targetLength = DEFAULT_LENGHT)
            => GetCountToReachLength(FragmentsMap, startFragmentId, targetLength);

        public int GetWordCountForLength(int startWordId, int targetLength = DEFAULT_LENGHT )
            => GetCountToReachLength(WordsMap, startWordId, targetLength);

        public int GetSentenceCountForLength(int startSentenceId, int targetLength = DEFAULT_LENGHT)
            => GetCountToReachLength(WordsSentances, startSentenceId, targetLength);

        // ------------------------------------------------------------
        // Alignment job splitting and anchor finding.
        // ------------------------------------------------------------
        public class AlignmentJob
        {
            public int WordStartIndex { get; set; }
            public int WordEndIndex { get; set; }
            public int WordCount => WordEndIndex - WordStartIndex;

            public int FragmentStartIndex { get; set; }
            public int FragmentEndIndex { get; set; }
            public int FragmentCount => FragmentEndIndex - FragmentStartIndex;
        }

        public void RunAlingment()
        {
            var rootJob = new AlignmentJob
            {
                WordStartIndex = 0,
                WordEndIndex = BookSegments.Count - 1,
                FragmentStartIndex = 0,
                FragmentEndIndex = TranscriptSegments.Count - 1
            };

            var jobQueue = new Stack<AlignmentJob>();
            jobQueue.Push(rootJob);

            while (jobQueue.Count > 0)
            {
                Console.WriteLine(jobQueue.Count);
                var currentJob = jobQueue.Pop();

                if (currentJob.FragmentCount <= 100)
                {
                    AlignMicroSegments(currentJob);
                    continue;
                }

                var subJobs = SplitJobIntoSmallerOnesByFindingAnchors(currentJob);
                if (subJobs.Count <= 1)
                {
                    AlignMicroSegments(currentJob);
                }
                else
                {
                    // Push subjobs in reverse order so they are processed start-to-finish
                    for (int i = subJobs.Count - 1; i >= 0; i--)
                    {
                        jobQueue.Push(subJobs[i]);
                    }
                }
            }

            SaveWordSegments(BookSegments, WordPath);

            var options = new JsonSerializerOptions { WriteIndented = true };
            string json = JsonSerializer.Serialize(_logs, options);
            if (File.Exists(LogPath))
                File.Delete(LogPath);
            File.WriteAllText(LogPath, json);
        }

        private List<AlignmentJob> SplitJobIntoSmallerOnesByFindingAnchors(AlignmentJob job)
        {
            var subJobs = new List<AlignmentJob>();
             int searchAttempts = (int)job.FragmentCount/50;

            var anchors = new List<(int FragIdx, int WordIdx)>
            {
                (job.FragmentStartIndex, job.WordStartIndex)
            };

            int step = job.FragmentCount / (searchAttempts + 1);

            for (int i = 1; i <= searchAttempts; i++)
            {
                int startFragIdx = job.FragmentStartIndex + (i * step);
                var result = FindFragmentSequenceMatchInWordRange(
                    startFragIdx,
                    Math.Max(job.WordStartIndex,job.WordStartIndex+ (job.WordCount/searchAttempts*2)),
                    job.WordEndIndex,
                    35);

                if (result.score > 80 && ValidateExpansion(result.score,startFragIdx,result.bestWord))
                {
                    anchors.Add((startFragIdx, result.bestWord));
                }
            }

            anchors.Add((job.FragmentEndIndex, job.WordEndIndex));

            for (int i = 0; i < anchors.Count - 1; i++)
            {
                subJobs.Add(new AlignmentJob
                {
                    FragmentStartIndex = anchors[i].FragIdx,
                    FragmentEndIndex = anchors[i + 1].FragIdx,
                    WordStartIndex = anchors[i].WordIdx,
                    WordEndIndex = anchors[i + 1].WordIdx
                });
            }

            return subJobs;
        }

        private const int EXPANSION_PASS_SCORE = 75;

        private (int bestWord, int score) FindFragmentSequenceMatchInWordRange(
            int startFragIdx,
            int wordSearchStart,
            int wordSearchEnd,
            int requiredQuickExitScore)
        {
            // 1. LONG WINDOW: Keep this as-is for high-confidence identification
            var anchorChars = GetFragmentChars(startFragIdx, startFragIdx + GetFragmentCountForLength(startFragIdx));
            string anchorStr = new string(anchorChars);
            int anchorLen = anchorChars.Length;

            int bestSentenceIdx = -1;
            int bestSentenceScore = 0;

            // Phase 1 – Coarse Scan (Unchanged)
            for (int s = 0; s < WordsSentances.Length; s++)
            {
                var sentence = WordsSentances[s];
                int sentanceCount = GetSentenceCountForLength(s);
                int sentenceLenght = WordsSentances[s + sentanceCount].EndId - sentence.StartId;

                if (sentence.FirstWordIndex < wordSearchStart - 100) continue;
                if (WordsSentances[s + sentanceCount].EndId > wordSearchEnd + 100) break;

                var sentenceSpan = words.AsSpan(sentence.StartId, sentenceLenght);
                int score = Fuzz.Ratio(anchorStr, new string(sentenceSpan));

                if (score >= EXPANSION_PASS_SCORE && ValidateExpansion(score, startFragIdx, sentence.FirstWordIndex, 500))
                {
                    if (score > bestSentenceScore)
                    {
                        bestSentenceScore = score;
                        bestSentenceIdx = s;
                    }
                }
            }

            if (bestSentenceIdx == -1 || bestSentenceScore < EXPANSION_PASS_SCORE)
                return (-1, 0);

            // Phase 2 – Long-Window Sliding to find exact start (Unchanged)
            var bestSentence = WordsSentances[bestSentenceIdx];
            int searchWordStart = Math.Max(wordSearchStart, bestSentence.FirstWordIndex - 20);
            int searchWordEnd = Math.Min(wordSearchEnd, bestSentence.LastWordIndex + 20);

            int bestScore = 0;
            int bestWord = -1;
            int windowLength = (int)(anchorLen * 1.1);

            for (int w = searchWordStart; w <= searchWordEnd; w++)
            {
                int charStart = WordsMap[w].StartId;
                if (charStart + windowLength > words.Length) break;

                var span = words.AsSpan(charStart, windowLength);
                int score = Fuzz.WeightedRatio(anchorStr, new string(span));

                if (score >= bestScore)
                {
                    bestScore = score;
                    bestWord = w;
                }
            }

            if (bestWord != -1)
            {
                // 2. SHORT MATCH: Now find the actual length of just the CURRENT fragment 
                // starting at the bestWord we just found.
                var shortMatch = MatchFragmentAtWordIndex(bestWord, startFragIdx, wordSearchEnd);

                LogOutcome(
                    fragmentIndex: startFragIdx,
                    level: LogLevel.Green,
                    message: $"Anchor found at word {bestWord} (Sentence {bestSentenceIdx}). Long-window score: {bestScore}%.",
                    wordPos: bestWord,
                    fragmentMap: FragmentsMap[startFragIdx],
                    matchedWordCount: shortMatch.wordCount);
            }
            if (bestScore < 75)
            {
                Console.WriteLine($"!!! LOW SCORE DETECTED at Word {bestWord} (Score: {bestScore}) !!!");
                System.Diagnostics.Debug.WriteLine("Investigating low score match...");
            }

            return (bestWord, bestScore);
        }
        public int GetSafeExpansionLength(int desiredLength, int wordStartChar, int wordSearchEnd)
        {
            int maxAvailable = wordSearchEnd - wordStartChar;
            return desiredLength > maxAvailable ? maxAvailable : desiredLength;
        }

        private const int EXPANSION_DEPTH = 100;
        private bool ValidateExpansion(int baseScore, int anchorFragIdx, int anchorWordIdx, int ExpamsionDepht = EXPANSION_DEPTH)
        {
                string fText = new string(GetFragmentChars(anchorFragIdx,anchorFragIdx + GetFragmentCountForLength(anchorFragIdx, ExpamsionDepht)));
            int x = GetWordCountForLength(anchorWordIdx, ExpamsionDepht);
                string wText = new string(GetWordChars(anchorWordIdx, anchorWordIdx + x));

                int score = Fuzz.Ratio(fText, wText);
                if (score < baseScore*0.9) return false;
            
            return true;
        }

        // ------------------------------------------------------------
        // Gap tracking (unchanged)
        // ------------------------------------------------------------
        public List<WordGap> wordGaps = new List<WordGap>();
        public List<FragmentGap> fragmentGaps = new List<FragmentGap>();

        public class WordGap { public int StartWordIndex; public int EndWordIndex; }
        public class FragmentGap { public int StartFragmentIndex; public int EndFragmentIndex; }

        public void AddAndMergeWordGap(int start, int end)
        {
            wordGaps.Add(new WordGap { StartWordIndex = start, EndWordIndex = end });
            wordGaps = wordGaps.OrderBy(g => g.StartWordIndex).ToList();

            for (int i = 0; i < wordGaps.Count - 1; i++)
            {
                if (wordGaps[i].EndWordIndex >= wordGaps[i + 1].StartWordIndex - 1)
                {
                    wordGaps[i].EndWordIndex = Math.Max(wordGaps[i].EndWordIndex, wordGaps[i + 1].EndWordIndex);
                    wordGaps.RemoveAt(i + 1);
                    i--;
                }
            }
        }

        public void AddAndMergeFragmentGap(int start, int end)
        {
            fragmentGaps.Add(new FragmentGap { StartFragmentIndex = start, EndFragmentIndex = end });
            fragmentGaps = fragmentGaps.OrderBy(g => g.StartFragmentIndex).ToList();

            for (int i = 0; i < fragmentGaps.Count - 1; i++)
            {
                if (fragmentGaps[i].EndFragmentIndex >= fragmentGaps[i + 1].StartFragmentIndex - 1)
                {
                    fragmentGaps[i].EndFragmentIndex = Math.Max(fragmentGaps[i].EndFragmentIndex, fragmentGaps[i + 1].EndFragmentIndex);
                    fragmentGaps.RemoveAt(i + 1);
                    i--;
                }
            }
        }

        // ------------------------------------------------------------
        // Micro‑alignment (now uses FuzzySharp)
        // ------------------------------------------------------------
        public void AlignMicroSegments(AlignmentJob job)
        {
            int lastWordIndex = job.WordStartIndex;
            int wordIndex = job.WordStartIndex;

            for (int i = job.FragmentStartIndex; i < job.FragmentEndIndex; i++)
            {
                var result = MatchFragmentAtWordIndex(wordIndex, i, job.WordEndIndex);

                // Backup strategy if score too low
                if (result.score < 60 && TranscriptSegments[i].NormalizedText.Length > 0)
                {
                    (int index, int score) backupResult = (0, 0);

                    var test = FindFragmentSequenceMatchInWordRange(i, wordIndex, job.WordEndIndex, 60);
                    if (test.score > 60)
                        backupResult = (test.bestWord, 9999);
                    else
                    {
                        AddAndMergeFragmentGap(i, i);
                        LogOutcome(i, LogLevel.Yellow, $"Failed to align fragment. Marking fragment gap at {i}", wordIndex, FragmentsMap[i], 0);
                    }

                    if (backupResult.score > 0)
                    {
                        if (backupResult.index - wordIndex < 10)
                        {
                            LogOutcome(i, LogLevel.Green, "Backup match close enough to apply directly", backupResult.index, FragmentsMap[i], result.wordCount);
                        }
                        else
                        {
                            AddAndMergeWordGap(wordIndex, backupResult.index);
                            LogOutcome(i, LogLevel.Yellow, $"Backup match found but with a gap. Marking word gap from {wordIndex} to {backupResult.index}", backupResult.index, FragmentsMap[i], result.wordCount);
                        }

                        wordIndex = backupResult.index;
                        result = MatchFragmentAtWordIndex(wordIndex, i, job.WordEndIndex);

                        ApplyMatch(i, wordIndex, result.wordCount);
                        wordIndex += result.wordCount;
                        continue;
                    }
                }
                else
                {


                // Standard handling
                if (result.wordCount == 0 && TranscriptSegments[i].NormalizedText.Length == 0)
                {
                    LogOutcome(i, LogLevel.Yellow, "Aligned empty fragment", wordIndex, FragmentsMap[i], 0);
                }
                else if (result.wordCount == 0)
                {
                    LogOutcome(i, LogLevel.Red, $"Total Failure at {i}", wordIndex, FragmentsMap[i], 0);
                }
                else
                {
                    LogOutcome(i, LogLevel.Green, $"Aligned (Score: {result.score}%)", wordIndex, FragmentsMap[i], result.wordCount);
                    ApplyMatch(i, wordIndex, result.wordCount);
                    wordIndex += result.wordCount;
                }


                }
            }
        }

        public void ApplyMatch(int i, int wordIndex, int wordCount)
        {
            var matchedFragment = TranscriptSegments[i];
            for (int j = wordIndex; j < wordCount + wordIndex; j++)
                BookSegments[j].LinkedSegments.Add(matchedFragment);
        }

        // ------------------------------------------------------------
        // Try to match a single fragment against 1..N words
        // ------------------------------------------------------------
        public (int wordCount, int score) MatchFragmentAtWordIndex(
            int startWordIndex,
            int fragmentIndex,
            int maxWordIndex)
        {
            // Target is only the current fragment (Short Text)
            var targetChars = GetFragmentChars(fragmentIndex, fragmentIndex);
            string targetStr = new string(targetChars);

            // Limit word search to a reasonable amount (e.g., 25 words) 
            // instead of character length to avoid the "mathing too much" bug.
            int wordLimit = Math.Min(maxWordIndex, startWordIndex + targetChars.Length);

            int bestScore = 0;
            int bestWordCount = 0;

            for (int i = 1; i <= (wordLimit - startWordIndex); i++)
            {
                // Get words from startWordIndex to (startWordIndex + i - 1)
                var wordChars = GetWordChars(startWordIndex, startWordIndex + i - 1);
                int score = Fuzz.Ratio(targetStr, new string(wordChars));

                // Bonus if word chunk ends with punctuation (helps align boundaries)
                if (wordChars.Length > 0 && (wordChars[wordChars.Length - 1] == '.' || wordChars[wordChars.Length - 1] == ','))
                    score += 5;

                if (score >= bestScore)
                {
                    bestScore = score;
                    bestWordCount = i;
                }
                else if (score < bestScore - 20)
                {
                    // If the score starts dropping significantly, we've overshot the fragment
                    break;
                }
            }
            return (bestWordCount, bestScore);
        }

        // ------------------------------------------------------------
        // Logging (adapted to char[] – simply create strings from spans)
        // ------------------------------------------------------------
        private static readonly ConcurrentQueue<LogEntry> _logs = new ConcurrentQueue<LogEntry>();

        private void LogOutcome(
            int fragmentIndex,
            LogLevel level,
            string message,
            int wordPos,
            AlingmentMapper fragmentMap,
            int matchedWordCount = 0)
        {

            string snippet = string.Empty;
            string matchedText = string.Empty;
            string targetText = string.Empty;
            Console.WriteLine("------------------------------------------");

                const int contextRadius = 50;
                int startWord = Math.Max(0, wordPos - contextRadius);
                int endWord = Math.Min(WordsMap.Length, wordPos + contextRadius);

                var snippetChars = GetWordChars(startWord, endWord);
                snippet = new string(snippetChars);
                Console.WriteLine("Context snippet text:    \n " + snippet);
                var matchedChars = GetWordChars(wordPos, wordPos + matchedWordCount);
                matchedText = new string(matchedChars);
                 Console.WriteLine("Matched text:    \n " + matchedText);

                var fragmentChars = GetFragmentChars(fragmentIndex, fragmentIndex);
                targetText = new string(fragmentChars);
                 Console.WriteLine("Target text:    \n "+targetText);
            
             Console.WriteLine("Message:\n"+message+"\n\n\n");
            Console.WriteLine("------------------------------------------");
            var entry = new LogEntry
            {
                FragmentIndex = fragmentIndex,
                StartPos = wordPos,
                Level = level,
                Message =  message,
                ContextSnippet = snippet,
                MachedText = matchedText,
                TargetText = targetText,
                IsSystemMessage = true
            };

            _logs.Enqueue(entry);
        }
    }

    // LogEntry definition (assumed to exist)
    public class LogEntry
    {
        public required int FragmentIndex { get; set; }
        public required int StartPos { get; set; }
        public required LogLevel Level { get; set; }
        public string Message { get; set; } = "No Message set";
        public string ContextSnippet { get; set; }= "No Context Snippet set";
        public string MachedText { get; set; } = "No Matched Text set";
        public string TargetText { get; set; }= "No Target Text set";
        public bool IsSystemMessage { get; set; } = false;
    }

    public enum LogLevel
    {
        Red,
        Yellow,
        Green
    }
}