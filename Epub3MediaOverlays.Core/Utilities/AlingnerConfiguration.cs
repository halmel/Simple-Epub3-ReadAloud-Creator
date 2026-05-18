namespace Epub3MediaOverlays.Core.AlingnerUtil
{
    /// <summary>
    /// Configuration class for the AlingnerNew alignment algorithm.
    /// Contains all tunable parameters for alignment behavior.
    /// </summary>
    public class AlingnerConfiguration
    {
        /// <summary>
        /// Maximum fragment count for a job to be considered a micro job (no splitting needed).
        /// Larger values = more aggressive optimization, smaller values = more granular splitting.
        /// </summary>
        public int MicroJobFragmentThreshold { get; set; } = 100;

        /// <summary>
        /// Anchor search divisor. Used to determine how many anchor points to search for.
        /// Fragments are divided by this value to calculate search attempts.
        /// </summary>
        public int AnchorSearchDivisor { get; set; } = 50;

        /// <summary>
        /// Required quick exit score for anchor finding. Fuzzy match score threshold for fast path.
        /// </summary>
        public int RequiredQuickExitScore { get; set; } = 35;

        /// <summary>
        /// Minimum fuzzy match score for an anchor to be considered valid.
        /// </summary>
        public int ValidAnchorScoreThreshold { get; set; } = 80;

        /// <summary>
        /// Default expansion depth used when validating anchor expansions.
        /// Controls how many characters to examine around an anchor point.
        /// </summary>
        public int ExpansionDepth { get; set; } = 100;

        /// <summary>
        /// Expansion depth for anchor validation during coarse scan phase.
        /// </summary>
        public int AnchorValidationExpansionDepth { get; set; } = 500;

        /// <summary>
        /// Sentence search buffer. How many words to look before/after the search range.
        /// Used in coarse scan to find nearby sentences.
        /// </summary>
        public int SentenceSearchBuffer { get; set; } = 100;

        /// <summary>
        /// Search word range adjustment. Used to narrow the search window during phase 2.
        /// </summary>
        public int SearchWordRangeAdjustment { get; set; } = 20;

        /// <summary>
        /// Window length multiplier for long-window sliding phase.
        /// Anchor length is multiplied by this factor to set search window size.
        /// </summary>
        public double WindowLengthMultiplier { get; set; } = 1.1;

        /// <summary>
        /// Minimum fuzzy match score threshold to pass expansion validation.
        /// </summary>
        public int ExpansionPassScore { get; set; } = 75;

        /// <summary>
        /// Score threshold below which a low score warning is logged during anchor finding.
        /// </summary>
        public int LowScoreDetectionThreshold { get; set; } = 75;

        /// <summary>
        /// Score validation ratio. Expanded text score must be at least baseScore * this value.
        /// Used when validating anchor expansions (typically 0.9 for 90%).
        /// </summary>
        public double ScoreValidationRatio { get; set; } = 0.9;

        /// <summary>
        /// Score threshold for triggering backup alignment strategy.
        /// If match score falls below this, attempts backup matching.
        /// </summary>
        public int BackupStrategyScoreThreshold { get; set; } = 60;

        /// <summary>
        /// Minimum score required for a backup match to be considered successful.
        /// </summary>
        public int BackupScoreRequirement { get; set; } = 60;

        /// <summary>
        /// Dummy score assigned to backup results to indicate they should be processed.
        /// </summary>
        public int BackupResultDummyScore { get; set; } = 9999;

        /// <summary>
        /// Maximum gap tolerance for backup matches. If the gap between old and new index
        /// is less than this, the match is applied directly without marking a gap.
        /// </summary>
        public int BackupGapTolerance { get; set; } = 10;

        /// <summary>
        /// Score drop threshold for early exit in fragment matching.
        /// If score drops by more than this amount, stop searching (we've overshot).
        /// </summary>
        public int ScoreDropThresholdForEarlyExit { get; set; } = 20;

        /// <summary>
        /// Bonus score applied when a matched word chunk ends with punctuation (. or ,).
        /// Helps align boundaries correctly.
        /// </summary>
        public int PunctuationBonusScore { get; set; } = 5;

        /// <summary>
        /// Default character length for counting segments.
        /// Used as the default target length in GetFragmentCountForLength, etc.
        /// </summary>
        public int DefaultSegmentLength { get; set; } = 50;
    }
}


