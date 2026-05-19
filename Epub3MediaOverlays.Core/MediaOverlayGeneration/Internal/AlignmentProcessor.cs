using FuzzySharp;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Epub3MediaOverlays.Core.MediaOverlayGeneration.Models;
using Epub3MediaOverlays.Core.MediaOverlayGeneration.Internal;

namespace Epub3MediaOverlays.Core.MediaOverlayGeneration.Internal
{
    /// <summary>
    /// Internal alignment processor that matches audio fragments to text segments.
    /// Implements the core alignment algorithm using fuzzy matching and heuristic anchoring.
    /// </summary>
    internal class AlignmentProcessor
    {
        public string WordPath { get; set; }
        public string LogPath { get; set; }
        public LogLevel MinLogLevel { get; set; } = LogLevel.Green;
        public AlignmentConfiguration Config { get; set; }

        public List<WordSegment> BookSegments { get; set; }
        public List<AudioFragment> TranscriptSegments { get; set; }

        public char[] words { get; }
        public AlignmentMapper[] WordsMap { get; set; }
        public AlignmentMapper[] WordsSentences { get; set; }

        public char[] fragments { get; }
        public AlignmentMapper[] FragmentsMap { get; set; }

        public AlignmentProcessor(ref List<WordSegment> bookSegments,
                                   ref List<AudioFragment> transcriptSegments,
                                   string wordPath, string logPath,
                                   AlignmentConfiguration config = null)
        {
            WordPath = wordPath;
            LogPath = logPath;
            MinLogLevel = LogLevel.Green;
            Config = config ?? new AlignmentConfiguration();
            BookSegments = bookSegments;
            TranscriptSegments = transcriptSegments;

            WordsMap = new AlignmentMapper[BookSegments.Count];
            for (int i = 0; i < BookSegments.Count; i++)
                WordsMap[i] = new AlignmentMapper();

            words = BuildCharArray(BookSegments, WordsMap, s => s.NormalizedWord);

            FragmentsMap = new AlignmentMapper[TranscriptSegments.Count];
            for (int i = 0; i < TranscriptSegments.Count; i++)
                FragmentsMap[i] = new AlignmentMapper();

            fragments = BuildCharArray(TranscriptSegments, FragmentsMap, s => s.NormalizedText);
            WordsSentences = BuildSentenceMapFromWords();
        }

        public class AlignmentMapper
        {
            public int StartId;
            public int EndId;
            public int ListIndex;

            public int Length => EndId - StartId;
            public int FirstWordIndex;
            public int LastWordIndex;
        }

        private static char[] BuildCharArray<T>(
            List<T> segments,
            AlignmentMapper[] map,
            Func<T, string> selector) where T : class
        {
            var result = new List<char>();
            int pos = 0;
            bool lastWasSpace = true;

            foreach (var segment in segments)
            {
                string text = selector(segment);
                int segIndex = (segment as dynamic).IndexInList;

                if (string.IsNullOrEmpty(text))
                {
                    map[segIndex] = new AlignmentMapper { StartId = pos, EndId = pos, ListIndex = segIndex };
                    continue;
                }

                if (!lastWasSpace && text[0] != '.')
                {
                    result.Add(' ');
                    pos++;
                }

                int startPos = pos;
                foreach (char ch in text)
                    result.Add(ch);

                pos += text.Length;
                map[segIndex] = new AlignmentMapper
                {
                    StartId = startPos,
                    EndId = pos,
                    ListIndex = segIndex
                };

                lastWasSpace = false;
            }

            return result.ToArray();
        }

        /// <summary>
        /// Normalizes text: lowercase letters kept, non-letters become spaces (except . and , which become .)
        /// </summary>
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

            return new string(buffer[..(lastWasSpace && pos > 0 ? pos - 1 : pos)]);
        }

        private AlignmentMapper[] BuildSentenceMapFromWords()
        {
            var sentences = new List<AlignmentMapper>();
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

                bool isEnd = word.EndId > word.StartId && words[word.EndId - 1] == '.';

                if (isEnd)
                {
                    sentences.Add(new AlignmentMapper
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

        private int GetCountToReachLength(AlignmentMapper[] map, int startIndex, int targetCharLength)
        {
            if (map == null || startIndex < 0 || startIndex >= map.Length - 1)
                return 0;

            int accumulatedLength = 0;
            int count = 0;

            for (int i = startIndex; i < map.Length - 1; i++)
            {
                accumulatedLength += map[i].Length;
                count++;

                if (accumulatedLength >= targetCharLength)
                    break;
            }

            return count;
        }

        public int GetFragmentCountForLength(int startFragmentId, int targetLength = -1)
            => GetCountToReachLength(FragmentsMap, startFragmentId, targetLength > 0 ? targetLength : Config.DefaultSegmentLength);

        public int GetWordCountForLength(int startWordId, int targetLength = -1)
            => GetCountToReachLength(WordsMap, startWordId, targetLength > 0 ? targetLength : Config.DefaultSegmentLength);

        public int GetSentenceCountForLength(int startSentenceId, int targetLength = -1)
            => GetCountToReachLength(WordsSentences, startSentenceId, targetLength > 0 ? targetLength : Config.DefaultSegmentLength);

        public class AlignmentJob
        {
            public int WordStartIndex { get; set; }
            public int WordEndIndex { get; set; }
            public int WordCount => WordEndIndex - WordStartIndex;

            public int FragmentStartIndex { get; set; }
            public int FragmentEndIndex { get; set; }
            public int FragmentCount => FragmentEndIndex - FragmentStartIndex;
        }

        public void RunAlignment()
        {
            var jobQueue = InitializeJobQueue();

            while (jobQueue.Count > 0)
            {
                Console.WriteLine(jobQueue.Count);
                var currentJob = jobQueue.Pop();

                if (IsMicroJob(currentJob))
                {
                    AlignMicroSegments(currentJob);
                    continue;
                }

                ProcessJob(jobQueue, currentJob);
            }

            FinalizeAlignment();
        }

        /// <summary>
        /// Alias for backward compatibility with existing code that calls RunAlingment.
        /// </summary>
        public void RunAlingment() => RunAlignment();

        private Stack<AlignmentJob> InitializeJobQueue()
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
            return jobQueue;
        }

        private bool IsMicroJob(AlignmentJob job)
        {
            return job.FragmentCount <= Config.MicroJobFragmentThreshold;
        }

        private void ProcessJob(Stack<AlignmentJob> jobQueue, AlignmentJob currentJob)
        {
            var subJobs = SplitJobIntoSmallerOnesByFindingAnchors(currentJob);

            if (subJobs.Count <= 1)
            {
                AlignMicroSegments(currentJob);
                return;
            }

            PushJobsInReverse(jobQueue, subJobs);
        }

        private void PushJobsInReverse(Stack<AlignmentJob> jobQueue, List<AlignmentJob> jobs)
        {
            for (int i = jobs.Count - 1; i >= 0; i--)
                jobQueue.Push(jobs[i]);
        }

        private void FinalizeAlignment()
        {
            EpubProcessor.SaveWordSegments(BookSegments, WordPath);

            var options = new JsonSerializerOptions { WriteIndented = true };
            string json = JsonSerializer.Serialize(_logs, options);

            if (File.Exists(LogPath))
                File.Delete(LogPath);

            File.WriteAllText(LogPath, json);
        }

        private List<AlignmentJob> SplitJobIntoSmallerOnesByFindingAnchors(AlignmentJob job)
        {
            var anchors = BuildAnchors(job);
            return BuildSubJobsFromAnchors(anchors);
        }

        private List<(int FragIdx, int WordIdx)> BuildAnchors(AlignmentJob job)
        {
            var anchors = new List<(int FragIdx, int WordIdx)>
            {
                (job.FragmentStartIndex, job.WordStartIndex)
            };

            int searchAttempts = (int)job.FragmentCount / Config.AnchorSearchDivisor;
            int step = job.FragmentCount / (searchAttempts + 1);

            for (int i = 1; i <= searchAttempts; i++)
            {
                int startFragIdx = job.FragmentStartIndex + (i * step);

                var result = FindFragmentSequenceMatchInWordRange(
                    startFragIdx,
                    Math.Max(job.WordStartIndex, job.WordStartIndex + (job.WordCount / searchAttempts * 2)),
                    job.WordEndIndex,
                    Config.RequiredQuickExitScore);

                if (IsValidAnchor(result, startFragIdx))
                {
                    anchors.Add((startFragIdx, result.bestWord));
                }
            }

            anchors.Add((job.FragmentEndIndex, job.WordEndIndex));
            return anchors;
        }

        private bool IsValidAnchor((int bestWord, int score) result, int fragmentIndex)
        {
            return result.score > Config.ValidAnchorScoreThreshold && ValidateExpansion(result.score, fragmentIndex, result.bestWord);
        }

        private List<AlignmentJob> BuildSubJobsFromAnchors(List<(int FragIdx, int WordIdx)> anchors)
        {
            var subJobs = new List<AlignmentJob>();

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

        private (int bestWord, int score) FindFragmentSequenceMatchInWordRange(
            int startFragIdx,
            int wordSearchStart,
            int wordSearchEnd,
            int requiredQuickExitScore)
        {
            var anchorChars = GetFragmentChars(startFragIdx, startFragIdx + GetFragmentCountForLength(startFragIdx));
            string anchorStr = new string(anchorChars);
            int anchorLen = anchorChars.Length;

            int bestSentenceIdx = -1;
            int bestSentenceScore = 0;

            for (int s = 0; s < WordsSentences.Length; s++)
            {
                var sentence = WordsSentences[s];
                int sentenceCount = GetSentenceCountForLength(s);
                int sentenceLength = WordsSentences[s + sentenceCount].EndId - sentence.StartId;

                if (sentence.FirstWordIndex < wordSearchStart - Config.SentenceSearchBuffer) continue;
                if (WordsSentences[s + sentenceCount].EndId > wordSearchEnd + Config.SentenceSearchBuffer) break;

                var sentenceSpan = words.AsSpan(sentence.StartId, sentenceLength);
                int score = Fuzz.Ratio(anchorStr, new string(sentenceSpan));

                if (score >= Config.ExpansionPassScore && ValidateExpansion(score, startFragIdx, sentence.FirstWordIndex, Config.AnchorValidationExpansionDepth))
                {
                    if (score > bestSentenceScore)
                    {
                        bestSentenceScore = score;
                        bestSentenceIdx = s;
                    }
                }
            }

            if (bestSentenceIdx == -1 || bestSentenceScore < Config.ExpansionPassScore)
                return (-1, 0);

            var bestSentence = WordsSentences[bestSentenceIdx];
            int searchWordStart = Math.Max(wordSearchStart, bestSentence.FirstWordIndex - Config.SearchWordRangeAdjustment);
            int searchWordEnd = Math.Min(wordSearchEnd, bestSentence.LastWordIndex + Config.SearchWordRangeAdjustment);

            int bestScore = 0;
            int bestWord = -1;
            int windowLength = (int)(anchorLen * Config.WindowLengthMultiplier);

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
            }

            return (bestWord, bestScore);
        }

        public int GetSafeExpansionLength(int desiredLength, int wordStartChar, int wordSearchEnd)
        {
            int maxAvailable = wordSearchEnd - wordStartChar;
            return desiredLength > maxAvailable ? maxAvailable : desiredLength;
        }

        private bool ValidateExpansion(int baseScore, int anchorFragIdx, int anchorWordIdx, int expansionDepth = -1)
        {
            if (expansionDepth < 0)
                expansionDepth = Config.ExpansionDepth;

            string fText = new string(GetFragmentChars(anchorFragIdx, anchorFragIdx + GetFragmentCountForLength(anchorFragIdx, expansionDepth)));
            int x = GetWordCountForLength(anchorWordIdx, expansionDepth);
            string wText = new string(GetWordChars(anchorWordIdx, anchorWordIdx + x));

            int score = Fuzz.Ratio(fText, wText);
            if (score < baseScore * Config.ScoreValidationRatio) return false;

            return true;
        }

        public List<WordGap> wordGaps = new List<WordGap>();
        public List<FragmentGap> fragmentGaps = new List<FragmentGap>();

        public class WordGap
        {
            public int StartWordIndex;
            public int EndWordIndex;
        }

        public class FragmentGap
        {
            public int StartFragmentIndex;
            public int EndFragmentIndex;
        }

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

        public void AlignMicroSegments(AlignmentJob job)
        {
            int wordIndex = job.WordStartIndex;

            for (int i = job.FragmentStartIndex; i < job.FragmentEndIndex; i++)
            {
                var result = MatchFragmentAtWordIndex(wordIndex, i, job.WordEndIndex);

                if (ShouldUseBackupStrategy(result, i))
                {
                    if (TryHandleBackup(ref wordIndex, i, job, ref result))
                        continue;
                }

                HandleStandardAlignment(i, ref wordIndex, result);
            }
        }

        private bool ShouldUseBackupStrategy((int wordCount, int score) result, int fragmentIndex)
        {
            return result.score < Config.BackupStrategyScoreThreshold && TranscriptSegments[fragmentIndex].NormalizedText.Length > 0;
        }

        private bool TryHandleBackup(ref int wordIndex, int fragmentIndex, AlignmentJob job, ref (int wordCount, int score) result)
        {
            (int index, int score) backupResult = (0, 0);

            var test = FindFragmentSequenceMatchInWordRange(fragmentIndex, wordIndex, job.WordEndIndex, Config.BackupScoreRequirement);
            if (test.score > Config.BackupScoreRequirement)
                backupResult = (test.bestWord, Config.BackupResultDummyScore);
            else
            {
                HandleFailedFragment(fragmentIndex, wordIndex);
            }

            if (backupResult.score > 0)
            {
                HandleBackupMatch(fragmentIndex, ref wordIndex, backupResult.index, result.wordCount);
                result = MatchFragmentAtWordIndex(wordIndex, fragmentIndex, job.WordEndIndex);
                ApplyMatch(fragmentIndex, wordIndex, result.wordCount);
                wordIndex += result.wordCount;
                return true;
            }

            return false;
        }

        private void HandleFailedFragment(int fragmentIndex, int wordIndex)
        {
            AddAndMergeFragmentGap(fragmentIndex, fragmentIndex);
            LogOutcome(fragmentIndex, LogLevel.Yellow, $"Failed to align fragment. Marking fragment gap at {fragmentIndex}", wordIndex, FragmentsMap[fragmentIndex], 0);
        }

        private void HandleBackupMatch(int fragmentIndex, ref int wordIndex, int newIndex, int originalWordCount)
        {
            if (newIndex - wordIndex < Config.BackupGapTolerance)
            {
                LogOutcome(fragmentIndex, LogLevel.Green, "Backup match close enough to apply directly", newIndex, FragmentsMap[fragmentIndex], originalWordCount);
            }
            else
            {
                AddAndMergeWordGap(wordIndex, newIndex);
                LogOutcome(fragmentIndex, LogLevel.Yellow, $"Backup match found but with a gap. Marking word gap from {wordIndex} to {newIndex}", newIndex, FragmentsMap[fragmentIndex], originalWordCount);
            }

            wordIndex = newIndex;
        }

        private void HandleStandardAlignment(int fragmentIndex, ref int wordIndex, (int wordCount, int score) result)
        {
            if (IsEmptyAlignment(fragmentIndex, result))
                return;

            if (result.wordCount == 0)
            {
                LogOutcome(fragmentIndex, LogLevel.Red, $"Total Failure at {fragmentIndex}", wordIndex, FragmentsMap[fragmentIndex], 0);
                return;
            }

            LogOutcome(fragmentIndex, LogLevel.Green, $"Aligned (Score: {result.score}%)", wordIndex, FragmentsMap[fragmentIndex], result.wordCount);
            ApplyMatch(fragmentIndex, wordIndex, result.wordCount);
            wordIndex += result.wordCount;
        }

        private bool IsEmptyAlignment(int fragmentIndex, (int wordCount, int score) result)
        {
            if (result.wordCount == 0 && TranscriptSegments[fragmentIndex].NormalizedText.Length == 0)
            {
                LogOutcome(fragmentIndex, LogLevel.Yellow, "Aligned empty fragment", 0, fragmentMap: FragmentsMap[fragmentIndex], matchedWordCount: 0);
                return true;
            }

            return false;
        }

        public void ApplyMatch(int i, int wordIndex, int wordCount)
        {
            var matchedFragment = TranscriptSegments[i];
            for (int j = wordIndex; j < wordCount + wordIndex; j++)
                BookSegments[j].LinkedSegments.Add(matchedFragment);
        }

        public (int wordCount, int score) MatchFragmentAtWordIndex(
            int startWordIndex,
            int fragmentIndex,
            int maxWordIndex)
        {
            var targetChars = GetFragmentChars(fragmentIndex, fragmentIndex);
            string targetStr = new string(targetChars);

            int wordLimit = Math.Min(maxWordIndex, startWordIndex + targetChars.Length);

            int bestScore = 0;
            int bestWordCount = 0;

            for (int i = 1; i <= (wordLimit - startWordIndex); i++)
            {
                var wordChars = GetWordChars(startWordIndex, startWordIndex + i - 1);
                int score = Fuzz.Ratio(targetStr, new string(wordChars));

                if (wordChars.Length > 0 && (wordChars[wordChars.Length - 1] == '.' || wordChars[wordChars.Length - 1] == ','))
                    score += Config.PunctuationBonusScore;

                if (score >= bestScore)
                {
                    bestScore = score;
                    bestWordCount = i;
                }
                else if (score < bestScore - Config.ScoreDropThresholdForEarlyExit)
                {
                    break;
                }
            }

            return (bestWordCount, bestScore);
        }

        private static readonly ConcurrentQueue<LogEntry> _logs = new ConcurrentQueue<LogEntry>();

        private void LogOutcome(
            int fragmentIndex,
            LogLevel level,
            string message,
            int wordPos,
            AlignmentMapper fragmentMap,
            int matchedWordCount = 0)
        {
            Console.WriteLine("------------------------------------------");

            string snippet = BuildContextSnippet(wordPos, 50);
            Console.WriteLine("Context snippet text:    \n " + snippet);

            string matchedText = BuildMatchedText(wordPos, matchedWordCount);
            Console.WriteLine("Matched text:    \n " + matchedText);

            string targetText = BuildTargetText(fragmentIndex);
            Console.WriteLine("Target text:    \n " + targetText);

            Console.WriteLine("Message:\n" + message + "\n\n\n");
            Console.WriteLine("------------------------------------------");

            var entry = new LogEntry
            {
                FragmentIndex = fragmentIndex,
                StartPos = wordPos,
                Level = level,
                Message = message,
                ContextSnippet = snippet,
                MatchedText = matchedText,
                TargetText = targetText,
                IsSystemMessage = true
            };

            _logs.Enqueue(entry);
        }

        private string BuildContextSnippet(int wordPos, int contextRadius)
        {
            int startWord = Math.Max(0, wordPos - contextRadius);
            int endWord = Math.Min(WordsMap.Length, wordPos + contextRadius);

            var snippetChars = GetWordChars(startWord, endWord);
            return new string(snippetChars);
        }

        private string BuildMatchedText(int wordPos, int matchedWordCount)
        {
            var matchedChars = GetWordChars(wordPos, wordPos + matchedWordCount);
            return new string(matchedChars);
        }

        private string BuildTargetText(int fragmentIndex)
        {
            var fragmentChars = GetFragmentChars(fragmentIndex, fragmentIndex);
            return new string(fragmentChars);
        }
    }
}

namespace Epub3MediaOverlays.Core.MediaOverlayGeneration.Models
{
    public class LogEntry
    {
        public required int FragmentIndex { get; set; }
        public required int StartPos { get; set; }
        public required LogLevel Level { get; set; }
        public string Message { get; set; } = "No Message set";
        public string ContextSnippet { get; set; } = "No Context Snippet set";
        public string MatchedText { get; set; } = "No Matched Text set";
        public string TargetText { get; set; } = "No Target Text set";
        public bool IsSystemMessage { get; set; } = false;
    }
}

namespace Epub3MediaOverlays.Core.MediaOverlayGeneration.Models
{
    public enum LogLevel
    {
        Red,
        Yellow,
        Green
    }
}