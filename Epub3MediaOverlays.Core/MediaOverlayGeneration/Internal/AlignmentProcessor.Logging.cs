using System.Collections.Concurrent;
using System.IO;
using System.Text.Json;
using Epub3MediaOverlays.Core.MediaOverlayGeneration;
using Epub3MediaOverlays.Core.MediaOverlayGeneration.Models;

namespace Epub3MediaOverlays.Core.MediaOverlayGeneration.Internal
{
    partial class AlignmentProcessor
    {
        private IAlignmentLogger _logger;
        private AlignmentLogNode _currentLogNode;
        private static readonly ConcurrentQueue<LogEntry> _logs = new ConcurrentQueue<LogEntry>();

        private void InitializeAlignmentLogger()
        {
            _logger.Initialize(0, BookSegments.Count - 1, 0, TranscriptSegments.Count - 1,
                               new string(words), new string(fragments));
        }

        private AlignmentLogNode CreateLogNodeForJob(AlignmentJob job)
        {
            var node = _logger.CreateSubJob(job.WordStartIndex, job.WordEndIndex,
                                            job.FragmentStartIndex, job.FragmentEndIndex);
            _currentLogNode = node;
            return node;
        }

        private void LogJobSplit()
        {
            if (_currentLogNode != null)
                _logger.LogSplit(_currentLogNode);
        }

        private void LogJobSuccess()
        {
            if (_currentLogNode != null)
                _logger.LogSuccess(_currentLogNode);
        }

        private void LogJobPartial(List<WordGap> wordGaps, List<FragmentGap> fragmentGaps)
        {
            if (_currentLogNode != null)
                _logger.LogPartial(_currentLogNode, wordGaps, fragmentGaps);
        }

        private void LogJobFailed(string errorMessage)
        {
            if (_currentLogNode != null)
                _logger.LogFailed(_currentLogNode, errorMessage);
        }

        private void LogFragmentAlignment(
            AlignmentLogNode logNode,
            int fragmentIndex,
            int wordStartIndex,
            int wordCount,
            int confidenceScore,
            AlignmentStatus status,
            string message = null,
            bool isEmptyFragment = false,
            string alignmentMethod = null)
        {
            try
            {
                var fragmentText = TranscriptSegments[fragmentIndex].NormalizedText;
                var matchedWordsText = wordCount > 0
                    ? new string(GetWordChars(wordStartIndex, wordStartIndex + wordCount - 1))
                    : "";

                var result = new FragmentAlignmentResult
                {
                    FragmentIndex = fragmentIndex,
                    WordStartIndex = wordStartIndex,
                    WordEndIndex = wordCount > 0 ? wordStartIndex + wordCount - 1 : wordStartIndex,
                    ConfidenceScore = confidenceScore,
                    Status = status,
                    ErrorMessage = message ?? BuildFragmentLogMessage(
                        status, fragmentIndex, wordStartIndex, wordCount, confidenceScore, isEmptyFragment, alignmentMethod),
                    FragmentText = fragmentText,
                    MatchedWordsText = matchedWordsText,
                    HasGaps = wordGaps.Count > 0 || fragmentGaps.Count > 0,
                    WordGapDetails = new List<WordGap>(wordGaps)
                };

                _logger.LogFragmentResult(logNode, result);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Warning: Failed to log fragment {fragmentIndex}: {ex.Message}");
            }
        }

        private static string BuildFragmentLogMessage(
            AlignmentStatus status,
            int fragmentIndex,
            int wordStartIndex,
            int wordCount,
            int confidenceScore,
            bool isEmptyFragment,
            string alignmentMethod)
        {
            switch (status)
            {
                case AlignmentStatus.Success when isEmptyFragment:
                    return $"Fragment {fragmentIndex}: empty transcript — skipped, no words consumed";

                case AlignmentStatus.Success:
                    var wordEnd = wordStartIndex + wordCount - 1;
                    var method = alignmentMethod ?? "sequential";
                    return $"Fragment {fragmentIndex}: aligned via {method} at words {wordStartIndex}–{wordEnd} ({wordCount} words, score {confidenceScore}%)";

                case AlignmentStatus.Failed:
                    return $"Fragment {fragmentIndex}: no words matched starting at word {wordStartIndex}";

                case AlignmentStatus.Gap when wordCount > 0:
                    return $"Fragment {fragmentIndex}: word gap — {wordCount} unmatched words at {wordStartIndex}–{wordStartIndex + wordCount - 1}";

                case AlignmentStatus.Gap:
                    var gapReason = alignmentMethod ?? "could not align";
                    return $"Fragment {fragmentIndex}: fragment gap — {gapReason} at word {wordStartIndex}";

                default:
                    return $"Fragment {fragmentIndex}: {status} at word {wordStartIndex}";
            }
        }

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

            _logs.Enqueue(new LogEntry
            {
                FragmentIndex = fragmentIndex,
                StartPos = wordPos,
                Level = level,
                Message = message,
                ContextSnippet = snippet,
                MatchedText = matchedText,
                TargetText = targetText,
                IsSystemMessage = true
            });
        }

        private string BuildContextSnippet(int wordPos, int contextRadius)
        {
            int startWord = Math.Max(0, wordPos - contextRadius);
            int endWord = Math.Min(WordsMap.Length, wordPos + contextRadius);
            return new string(GetWordChars(startWord, endWord));
        }

        private string BuildMatchedText(int wordPos, int matchedWordCount)
        {
            return new string(GetWordChars(wordPos, wordPos + matchedWordCount));
        }

        private string BuildTargetText(int fragmentIndex)
        {
            return new string(GetFragmentChars(fragmentIndex, fragmentIndex));
        }

        private void WriteAlignmentLog()
        {
            var logTree = _logger.Finalize();
            var options = new JsonSerializerOptions { WriteIndented = true };
            string json = JsonSerializer.Serialize(logTree, options);

            if (File.Exists(LogPath))
                File.Delete(LogPath);

            File.WriteAllText(LogPath, json);
        }
    }
}
