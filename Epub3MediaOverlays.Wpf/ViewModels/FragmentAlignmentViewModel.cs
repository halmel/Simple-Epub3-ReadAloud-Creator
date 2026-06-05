using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Media;
using Epub3MediaOverlays.Core.MediaOverlayGeneration;

namespace Epub3MediaOverlays.Wpf.ViewModels
{
    public class AlignmentPairViewModel
    {
        public int FragmentIndex { get; set; }
        public int WordStartIndex { get; set; }
        public int WordEndIndex { get; set; }
        public int ConfidenceScore { get; set; }
        public string MatchStatus { get; set; } = string.Empty;
        public string WordText { get; set; } = string.Empty;
        public string WordTooltip { get; set; } = string.Empty;
        public string FragmentText { get; set; } = string.Empty;
        public string FragmentTooltip { get; set; } = string.Empty;
        public Brush BackgroundColor { get; set; } = Brushes.Transparent;
        public Brush BorderColor { get; set; } = Brushes.Gray;
    }

    /// <summary>
    /// Represents a single block of aligned text (word or fragment) with metadata.
    /// </summary>
    public class AlignedTextBlock
    {
        /// <summary>The actual text content of this block.</summary>
        public string Text { get; set; }

        /// <summary>Start index in the source (word or fragment index).</summary>
        public int StartIndex { get; set; }

        /// <summary>End index in the source (word or fragment index).</summary>
        public int EndIndex { get; set; }

        /// <summary>Confidence score (0-100) for this alignment.</summary>
        public int ConfidenceScore { get; set; }

        /// <summary>Status of this alignment.</summary>
        public AlignmentStatus Status { get; set; }

        /// <summary>Error message if any.</summary>
        public string ErrorMessage { get; set; }

        /// <summary>Background color based on confidence.</summary>
        public Brush BackgroundColor { get; set; }

        /// <summary>Tooltip text with full details.</summary>
        public string TooltipText { get; set; }
    }

    /// <summary>
    /// View model for split-pane fragment alignment visualization.
    /// Shows words and fragments side-by-side with confidence-based coloring.
    /// </summary>
    public class FragmentAlignmentViewModel
    {
        public AlignmentLogNode LogNode { get; }
        public string OriginalWordText { get; }
        public string OriginalFragmentText { get; }

        public List<AlignedTextBlock> WordBlocks { get; } = new();
        public List<AlignedTextBlock> FragmentBlocks { get; } = new();
        public List<AlignmentPairViewModel> AlignmentPairs { get; } = new();

        public string JobSummary { get; }
        public int TotalFragments { get; }
        public int SuccessfulFragments { get; }
        public int FailedFragments { get; }
        public double SuccessRate { get; }

        public FragmentAlignmentViewModel(AlignmentLogNode logNode, string originalWordText, string originalFragmentText)
        {
            LogNode = logNode;
            OriginalWordText = originalWordText;
            OriginalFragmentText = originalFragmentText;

            // Build text blocks from fragment results
            BuildTextBlocks();

            // Calculate statistics
            TotalFragments = logNode.FragmentResults.Count;
            SuccessfulFragments = logNode.FragmentResults.Count(r => r.Status == AlignmentStatus.Success);
            FailedFragments = logNode.FragmentResults.Count(r => r.Status == AlignmentStatus.Failed);
            SuccessRate = TotalFragments > 0 ? (double)SuccessfulFragments / TotalFragments * 100 : 0;

            JobSummary = $"Micro-Job: {TotalFragments} fragments | {SuccessfulFragments} successful " +
                        $"({SuccessRate:F1}%) | {FailedFragments} failed";
        }

        private void BuildTextBlocks()
        {
            if (LogNode.FragmentResults.Count == 0)
                return;

            // Word blocks: group consecutive matched words
            var wordGaps = new HashSet<int>();
            foreach (var gap in LogNode.WordGaps)
            {
                for (int i = gap.StartWordIndex; i <= gap.EndWordIndex; i++)
                    wordGaps.Add(i);
            }

            int wordPos = LogNode.WordStartIndex;
            foreach (var fragmentResult in LogNode.FragmentResults)
            {
                // Add any gap words before this fragment's match
                while (wordPos < fragmentResult.WordStartIndex)
                {
                    var gapBlock = CreateGapWordBlock(wordPos);
                    WordBlocks.Add(gapBlock);
                    wordPos++;
                }

                // Add matched words
                var wordBlock = CreateWordBlock(fragmentResult);
                WordBlocks.Add(wordBlock);
                wordPos = fragmentResult.WordEndIndex + 1;
            }

            // Add trailing gap words
            while (wordPos <= LogNode.WordEndIndex)
            {
                var gapBlock = CreateGapWordBlock(wordPos);
                WordBlocks.Add(gapBlock);
                wordPos++;
            }

            // Fragment blocks: one per fragment result
            foreach (var result in LogNode.FragmentResults)
            {
                var fragmentBlock = CreateFragmentBlock(result);
                FragmentBlocks.Add(fragmentBlock);
                AlignmentPairs.Add(CreateAlignmentPair(result));
            }
        }

        private AlignmentPairViewModel CreateAlignmentPair(FragmentAlignmentResult result)
        {
            var background = GetConfidenceColor(result.ConfidenceScore);
            return new AlignmentPairViewModel
            {
                FragmentIndex = result.FragmentIndex,
                WordStartIndex = result.WordStartIndex,
                WordEndIndex = result.WordEndIndex,
                ConfidenceScore = result.ConfidenceScore,
                MatchStatus = result.Status.ToString(),
                WordText = string.IsNullOrWhiteSpace(result.MatchedWordsText) ? "[No matched words]" : result.MatchedWordsText,
                WordTooltip = BuildWordBlockTooltip(result),
                FragmentText = string.IsNullOrWhiteSpace(result.FragmentText) ? "[Empty fragment]" : result.FragmentText,
                FragmentTooltip = BuildFragmentBlockTooltip(result),
                BackgroundColor = background,
                BorderColor = GetStatusBorderColor(result.Status)
            };
        }

        private AlignedTextBlock CreateWordBlock(FragmentAlignmentResult fragmentResult)
        {
            var block = new AlignedTextBlock
            {
                Text = fragmentResult.MatchedWordsText,
                StartIndex = fragmentResult.WordStartIndex,
                EndIndex = fragmentResult.WordEndIndex,
                ConfidenceScore = fragmentResult.ConfidenceScore,
                Status = fragmentResult.Status,
                ErrorMessage = fragmentResult.ErrorMessage,
                BackgroundColor = GetConfidenceColor(fragmentResult.ConfidenceScore),
                TooltipText = BuildWordBlockTooltip(fragmentResult)
            };
            return block;
        }

        private AlignedTextBlock CreateFragmentBlock(FragmentAlignmentResult result)
        {
            var block = new AlignedTextBlock
            {
                Text = result.FragmentText,
                StartIndex = result.FragmentIndex,
                EndIndex = result.FragmentIndex,
                ConfidenceScore = result.ConfidenceScore,
                Status = result.Status,
                ErrorMessage = result.ErrorMessage,
                BackgroundColor = GetConfidenceColor(result.ConfidenceScore),
                TooltipText = BuildFragmentBlockTooltip(result)
            };
            return block;
        }

        private AlignedTextBlock CreateGapWordBlock(int wordIndex)
        {
            return new AlignedTextBlock
            {
                Text = "[GAP]",
                StartIndex = wordIndex,
                EndIndex = wordIndex,
                ConfidenceScore = 0,
                Status = AlignmentStatus.Failed,
                BackgroundColor = Brushes.LightCoral,
                TooltipText = $"Unaligned word gap at index {wordIndex}"
            };
        }

        private string BuildWordBlockTooltip(FragmentAlignmentResult result)
        {
            return $"Fragment {result.FragmentIndex} → Words {result.WordStartIndex}-{result.WordEndIndex}\n" +
                   $"Confidence: {result.ConfidenceScore}%\n" +
                   $"Status: {result.Status}\n" +
                   $"Fragment: \"{result.FragmentText}\"\n" +
                   $"Matched: \"{result.MatchedWordsText}\"" +
                   (string.IsNullOrEmpty(result.ErrorMessage) ? "" : $"\nError: {result.ErrorMessage}");
        }

        private string BuildFragmentBlockTooltip(FragmentAlignmentResult result)
        {
            return $"Fragment {result.FragmentIndex}\n" +
                   $"Matched to Words: {result.WordStartIndex}-{result.WordEndIndex} ({result.MatchedWordCount} words)\n" +
                   $"Confidence: {result.ConfidenceScore}%\n" +
                   $"Status: {result.Status}\n" +
                   $"Text: \"{result.FragmentText}\"" +
                   (string.IsNullOrEmpty(result.ErrorMessage) ? "" : $"\nError: {result.ErrorMessage}");
        }

        /// <summary>
        /// Maps confidence score (0-100) to a color gradient:
        /// Red (0%) -> Yellow (50%) -> Green (100%)
        /// </summary>
        private Brush GetConfidenceColor(int confidenceScore)
        {
            if (confidenceScore < 0) confidenceScore = 0;
            if (confidenceScore > 100) confidenceScore = 100;

            if (confidenceScore < 50)
            {
                // Red to Yellow: 0-50%
                double ratio = confidenceScore / 50.0;
                byte r = 255;
                byte g = (byte)(255 * ratio);
                byte b = 0;
                return new SolidColorBrush(Color.FromArgb(255, r, g, b));
            }
            else
            {
                // Yellow to Green: 50-100%
                double ratio = (confidenceScore - 50) / 50.0;
                byte r = (byte)(255 * (1 - ratio));
                byte g = 255;
                byte b = 0;
                return new SolidColorBrush(Color.FromArgb(255, r, g, b));
            }
        }

        private Brush GetStatusBorderColor(AlignmentStatus status)
        {
            return status switch
            {
                AlignmentStatus.Success => Brushes.ForestGreen,
                AlignmentStatus.Partial => Brushes.DarkOrange,
                AlignmentStatus.Failed => Brushes.IndianRed,
                AlignmentStatus.Split => Brushes.SteelBlue,
                _ => Brushes.Gray
            };
        }
    }
}
