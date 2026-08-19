using FuzzySharp;
using Epub3MediaOverlays.Core.MediaOverlayGeneration;
using Epub3MediaOverlays.Core.MediaOverlayGeneration.Models;

namespace Epub3MediaOverlays.Core.MediaOverlayGeneration.Internal
{
    partial class AlignmentProcessor
    {
        public class AlignmentJob
        {
            public int WordStartIndex { get; set; }
            public int WordEndIndex { get; set; }
            public int WordCount => WordEndIndex - WordStartIndex;

            public int FragmentStartIndex { get; set; }
            public int FragmentEndIndex { get; set; }
            public int FragmentCount => FragmentEndIndex - FragmentStartIndex;

            public AlignmentLogNode LogNode { get; set; }
        }

        public void RunAlignment()
        {
            var jobQueue = InitializeJobQueue();

            while (jobQueue.Count > 0)
            {
                Console.WriteLine(jobQueue.Count);
                var currentJob = jobQueue.Pop();
                _currentLogNode = currentJob.LogNode;

                if (IsMicroJob(currentJob))
                {
                    AlignMicroSegments(currentJob);
                    continue;
                }

                ProcessJob(jobQueue, currentJob);
            }

            FinalizeAlignment();
        }

        private Stack<AlignmentJob> InitializeJobQueue()
        {
            var rootJob = new AlignmentJob
            {
                WordStartIndex = 0,
                WordEndIndex = BookSegments.Count - 1,
                FragmentStartIndex = 0,
                FragmentEndIndex = TranscriptSegments.Count - 1
            };

            rootJob.LogNode = _logger.CreateSubJob(rootJob.WordStartIndex, rootJob.WordEndIndex,
                                                   rootJob.FragmentStartIndex, rootJob.FragmentEndIndex);

            var jobQueue = new Stack<AlignmentJob>();
            jobQueue.Push(rootJob);
            return jobQueue;
        }

        private bool IsMicroJob(AlignmentJob job)
            => job.FragmentCount <= Config.MicroJobFragmentThreshold;

        private void ProcessJob(Stack<AlignmentJob> jobQueue, AlignmentJob currentJob)
        {
            var subJobs = SplitJobIntoSmallerOnesByFindingAnchors(currentJob);

            if (subJobs.Count <= 1)
            {
                AlignMicroSegments(currentJob);
                return;
            }

            _logger.LogSplit(currentJob.LogNode);

            foreach (var subJob in subJobs)
            {
                subJob.LogNode = _logger.CreateSubJob(subJob.WordStartIndex, subJob.WordEndIndex,
                                                      subJob.FragmentStartIndex, subJob.FragmentEndIndex);
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
            SaveAlignmentResults();
            WriteAlignmentLog();
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
                    anchors.Add((startFragIdx, result.bestWord));
            }

            anchors.Add((job.FragmentEndIndex, job.WordEndIndex));
            return anchors;
        }

        private bool IsValidAnchor((int bestWord, int score) result, int fragmentIndex)
            => result.score > Config.ValidAnchorScoreThreshold && ValidateExpansion(result.score, fragmentIndex, result.bestWord);

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

            // Track top N candidates for fallback validation
            var topCandidates = new List<(int idx, int score, int firstWordIdx)>();
            const int maxCandidates = 5;

            // Fast path: sentence-level fuzzy match
            for (int s = 0; s < WordsSentences.Length; s++)
            {
                var sentence = WordsSentences[s];
                int sentenceCount = GetSentenceCountForLength(s);
                int sentenceLength = WordsSentences[s + sentenceCount].EndId - sentence.StartId;

                if (sentence.FirstWordIndex < wordSearchStart - Config.SentenceSearchBuffer) continue;
                if (WordsSentences[s + sentenceCount].EndId > wordSearchEnd + Config.SentenceSearchBuffer) break;

                var sentenceSpan = words.AsSpan(sentence.StartId, sentenceLength);
                int score = Fuzz.Ratio(anchorStr, new string(sentenceSpan));

                // Always track the best score, even below threshold
                if (score > 0)
                {
                    // Insert into sorted list of top candidates
                    bool inserted = false;
                    for (int i = 0; i < topCandidates.Count; i++)
                    {
                        if (score > topCandidates[i].score)
                        {
                            topCandidates.Insert(i, (s, score, sentence.FirstWordIndex));
                            inserted = true;
                            break;
                        }
                    }
                    if (!inserted)
                        topCandidates.Add((s, score, sentence.FirstWordIndex));
                    
                    if (topCandidates.Count > maxCandidates)
                        topCandidates.RemoveAt(topCandidates.Count - 1);
                }

                // Try validation for scores that meet threshold
                if (score >= Config.ExpansionPassScore && ValidateExpansion(score, startFragIdx, sentence.FirstWordIndex, Config.AnchorValidationExpansionDepth))
                {
                    // Found a valid candidate - proceed with word-level search
                    return PerformWordLevelSearch(s, score, startFragIdx, anchorStr, anchorLen, 
                                                 wordSearchStart, wordSearchEnd, sentence);
                }
            }

            // Fallback #3: Try validating top N candidates even if they didn't pass initially
            foreach (var candidate in topCandidates)
            {
                if (candidate.score >= Config.ExpansionPassScore)
                    continue; // Already tried these above

                var sentence = WordsSentences[candidate.idx];
                if (ValidateExpansion(candidate.score, startFragIdx, candidate.firstWordIdx, Config.AnchorValidationExpansionDepth))
                {
                    return PerformWordLevelSearch(candidate.idx, candidate.score, startFragIdx, anchorStr, anchorLen,
                                                 wordSearchStart, wordSearchEnd, sentence);
                }
            }
            int windowLength = (int)(anchorLen * Config.WindowLengthMultiplier);

     

            // Fallback #2: Return best sentence-level candidate even below threshold
            if (topCandidates.Count > 0)
            {
                var bestCandidate = topCandidates[0];
                var bestSentence = WordsSentences[bestCandidate.idx];
                int searchWordStart = Math.Max(wordSearchStart, bestSentence.FirstWordIndex - Config.SearchWordRangeAdjustment);
                int searchWordEnd = Math.Min(wordSearchEnd, bestSentence.LastWordIndex + Config.SearchWordRangeAdjustment);

                int bestScore = 0;
                int bestWord = -1;

                for (int w = searchWordStart; w <= searchWordEnd; w++)
                {
                    int charStart = WordsMap[w].StartId;
                    if (charStart + windowLength > words.Length) break;

                    var span = words.AsSpan(charStart, windowLength);
                    int score = Fuzz.WeightedRatio(anchorStr, new string(span));

                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestWord = w;
                    }
                }

                if (bestWord != -1)
                {
                    LogOutcome(
                        fragmentIndex: startFragIdx,
                        level: LogLevel.Yellow,
                        message: $"Using best available match at word {bestWord} (Sentence {bestCandidate.idx}, score: {bestScore}%, threshold: {Config.ExpansionPassScore}%)",
                        wordPos: bestWord,
                        fragmentMap: FragmentsMap[startFragIdx],
                        matchedWordCount: 0);
                }

                return (bestWord, bestScore);
            }

            // Complete failure - no match found
            return (-1, 0);
        }

        private (int bestWord, int score) PerformWordLevelSearch(
            int sentenceIdx,
            int sentenceScore,
            int startFragIdx,
            string anchorStr,
            int anchorLen,
            int wordSearchStart,
            int wordSearchEnd,
            AlignmentMapper bestSentence)
        {
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
                    message: $"Anchor found at word {bestWord} (Sentence {sentenceIdx}). Long-window score: {bestScore}%.",
                    wordPos: bestWord,
                    fragmentMap: FragmentsMap[startFragIdx],
                    matchedWordCount: shortMatch.wordCount);
            }

            if (bestScore < 75)
                Console.WriteLine($"!!! LOW SCORE DETECTED at Word {bestWord} (Score: {bestScore}) !!!");

            return (bestWord, bestScore);
        }

        /// <summary>
        /// Calculates an adaptive threshold based on fragment length.
        /// Longer fragments naturally produce higher fuzzy scores, while short fragments are noisier.
        /// </summary>
        private int GetAdaptiveThreshold(int fragmentLength)
        {
            if (fragmentLength <= 5)
                return 60;
            else if (fragmentLength <= 15)
                return 70;
            else if (fragmentLength <= 40)
                return 80;
            else
                return 90;
        }

        private bool ValidateExpansion(int baseScore, int anchorFragIdx, int anchorWordIdx, int expansionDepth = -1)
        {
            if (expansionDepth < 0)
                expansionDepth = Config.ExpansionDepth;

            string fText = new string(GetFragmentChars(anchorFragIdx, anchorFragIdx + GetFragmentCountForLength(anchorFragIdx, expansionDepth)));
            int x = GetWordCountForLength(anchorWordIdx, expansionDepth);
            string wText = new string(GetWordChars(anchorWordIdx, anchorWordIdx + x));

            int score = Fuzz.Ratio(fText, wText);
            
            // Use adaptive threshold based on fragment length
            int adaptiveThreshold = GetAdaptiveThreshold(fText.Length);
            int effectiveThreshold = Math.Max((int)(baseScore * Config.ScoreValidationRatio), adaptiveThreshold);
            
            return score >= effectiveThreshold;
        }

        /// <summary>
        /// Simplified backup search function optimized for small search ranges.
        /// Unlike FindFragmentSequenceMatchInWordRange which uses sentence-level optimization
        /// for large search areas, this function performs a direct word-by-word search
        /// which is more efficient for the small ranges typical in backup matching.
        /// </summary>
        private (int bestWord, int score) FindFragmentMatchInWordRangeForBackup(
            int startFragIdx,
            int wordSearchStart,
            int wordSearchEnd,
            int requiredQuickExitScore)
        {
            var fragmentChars = GetFragmentChars(startFragIdx, startFragIdx + GetFragmentCountForLength(startFragIdx));
            string fragmentStr = new string(fragmentChars);
            int fragmentLen = fragmentChars.Length;

            int bestScore = 0;
            int bestWord = -1;
            int windowLength = (int)(fragmentLen * Config.WindowLengthMultiplier);

            // Direct word-by-word search without sentence-level optimization
            // This is efficient for small search ranges typical in backup matching
            for (int w = wordSearchStart; w <= wordSearchEnd; w++)
            {
                int charStart = WordsMap[w].StartId;
                if (charStart + windowLength > words.Length) break;

                var span = words.AsSpan(charStart, windowLength);
                int score = Fuzz.WeightedRatio(fragmentStr, new string(span));

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
                    level: LogLevel.Yellow,
                    message: $"Backup match found at word {bestWord}. Window score: {bestScore}%.",
                    wordPos: bestWord,
                    fragmentMap: FragmentsMap[startFragIdx],
                    matchedWordCount: shortMatch.wordCount);
            }

            return (bestWord, bestScore);
        }

        public void AlignMicroSegments(AlignmentJob job)
        {
            int wordIndex = job.WordStartIndex;
            var jobWordGaps = new List<WordGap>();
            var jobFragmentGaps = new List<FragmentGap>();

            for (int i = job.FragmentStartIndex; i < job.FragmentEndIndex; i++)
            {
                CheckDebugBreakpoint(i, wordIndex);

                var result = MatchFragmentAtWordIndex(wordIndex, i, job.WordEndIndex);

                if (ShouldUseBackupStrategy(result, i))
                {
                    if (TryHandleBackup(ref wordIndex, i, job, ref result))
                    {
                        LogFragmentAlignment(job.LogNode, i, wordIndex - result.wordCount, result.wordCount, result.score,
                            AlignmentStatus.Success, alignmentMethod: "backup search");
                        continue;
                    }

                    continue;
                }

                HandleStandardAlignment(i, ref wordIndex, result, job.LogNode);
            }

            if (wordIndex < job.WordEndIndex)
            {
                AddAndMergeWordGap(wordIndex, job.WordEndIndex);
                LogOutcome(
                    fragmentIndex: job.FragmentEndIndex - 1,
                    level: LogLevel.Yellow,
                    message: $"Remaining unmatched words at end of micro job. Marking word gap from {wordIndex} to {job.WordEndIndex}",
                    wordPos: wordIndex,
                    fragmentMap: null,
                    matchedWordCount: 0);

                var remainingWordsText = wordIndex < WordsMap.Length
                    ? new string(GetWordChars(wordIndex, job.WordEndIndex))
                    : "";

                LogFragmentAlignment(
                    job.LogNode,
                    job.FragmentEndIndex - 1,
                    wordIndex,
                    job.WordEndIndex - wordIndex,
                    0,
                    AlignmentStatus.Gap,
                    $"Micro-job tail: {job.WordEndIndex - wordIndex} unmatched words at {wordIndex}–{job.WordEndIndex} — \"{remainingWordsText}\"",
                    isEmptyFragment: false,
                    alignmentMethod: "tail gap");
            }

            if (wordGaps.Count > 0 || fragmentGaps.Count > 0)
            {
                jobWordGaps.AddRange(wordGaps);
                jobFragmentGaps.AddRange(fragmentGaps);
                _logger.LogPartial(job.LogNode, jobWordGaps, jobFragmentGaps);
            }
            else
            {
                _logger.LogSuccess(job.LogNode);
            }

            wordGaps.Clear();
            fragmentGaps.Clear();
        }

        private bool ShouldUseBackupStrategy((int wordCount, int score) result, int fragmentIndex)
            => result.score < Config.BackupStrategyScoreThreshold && TranscriptSegments[fragmentIndex].NormalizedText.Length > 0;

        private bool TryHandleBackup(ref int wordIndex, int fragmentIndex, AlignmentJob job, ref (int wordCount, int score) result)
        {
            var searchResult = FindFragmentMatchInWordRangeForBackup(
                fragmentIndex,
                wordIndex,
                job.WordEndIndex,
                Config.BackupScoreRequirement - 20);

            if (searchResult.score >= Config.BackupScoreRequirement -20 && searchResult.bestWord > wordIndex)
            {
                int foundWordIndex = searchResult.bestWord;

                if (foundWordIndex > wordIndex)
                {
                    AddAndMergeWordGap(wordIndex, foundWordIndex);
                    LogOutcome(
                        fragmentIndex,
                        LogLevel.Yellow,
                        $"Fragment found later at word {foundWordIndex}. Marking word gap from {wordIndex} to {foundWordIndex}",
                        foundWordIndex,
                        FragmentsMap[fragmentIndex],
                        0);
                }

                wordIndex = foundWordIndex;
                result = MatchFragmentAtWordIndex(wordIndex, fragmentIndex, job.WordEndIndex);

                if (result.wordCount > 0 && result.score >= Config.BackupScoreRequirement)
                {
                    ApplyMatch(fragmentIndex, wordIndex, result.wordCount);
                    wordIndex += result.wordCount;
                    return true;
                }

                HandleFailedFragment(fragmentIndex, wordIndex);
                return false;
            }

            HandleFailedFragment(fragmentIndex, wordIndex);
            return false;
        }

        private void HandleFailedFragment(int fragmentIndex, int wordIndex)
        {
            AddAndMergeFragmentGap(fragmentIndex, fragmentIndex);
            LogOutcome(fragmentIndex, LogLevel.Yellow, $"Failed to align fragment. Marking fragment gap at {fragmentIndex}", wordIndex, FragmentsMap[fragmentIndex], 0);

            LogFragmentAlignment(
                _currentLogNode,
                fragmentIndex,
                wordIndex,
                0,
                0,
                AlignmentStatus.Gap,
                alignmentMethod: "backup search failed");
        }

        private void HandleStandardAlignment(int fragmentIndex, ref int wordIndex, (int wordCount, int score) result, AlignmentLogNode logNode)
        {
            if (IsEmptyAlignment(fragmentIndex, result))
            {
                LogFragmentAlignment(logNode, fragmentIndex, wordIndex, 0, 0, AlignmentStatus.Success,
                    isEmptyFragment: true, alignmentMethod: "empty fragment");
                return;
            }

            if (result.wordCount == 0)
            {
                LogFragmentAlignment(logNode, fragmentIndex, wordIndex, 0, 0, AlignmentStatus.Failed,
                    alignmentMethod: "sequential");
                LogOutcome(fragmentIndex, LogLevel.Red, $"Total Failure at {fragmentIndex}", wordIndex, FragmentsMap[fragmentIndex], 0);
                return;
            }

            LogFragmentAlignment(logNode, fragmentIndex, wordIndex, result.wordCount, result.score, AlignmentStatus.Success,
                alignmentMethod: "sequential");
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

        /// <summary>
        /// Matches a fragment to words starting at a given index, using a "3 consecutive misses" 
        /// early exit strategy instead of aggressive score-drop threshold. This is more tolerant
        /// of non-monotonic score patterns where scores may dip and then recover.
        /// </summary>
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
            int consecutiveMisses = 0;
            const int maxConsecutiveMisses = 3;

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
                    consecutiveMisses = 0; // Reset on improvement
                }
                else
                {
                    consecutiveMisses++;
                }

                // Early exit after 3 consecutive misses (scores not improving)
                if (consecutiveMisses >= maxConsecutiveMisses)
                    break;
            }

            return (bestWordCount, bestScore);
        }
    }
}