using Epub3MediaOverlays.Core.MediaOverlayGeneration.Internal;

namespace Epub3MediaOverlays.Core.MediaOverlayGeneration
{
    /// <summary>
    /// Configuration settings for the Media Overlay Generator.
    /// Specifies the transcription script and alignment configuration to use.
    /// </summary>
    public class MediaOverlayGeneratorSettings
    {
        /// <summary>
        /// The transcription script implementation (CUDA, CPU, etc.).
        /// Required to convert audio files to text.
        /// </summary>
        public required ITranscriptionScript TranscriptionScript { get; set; }

        /// <summary>
        /// Alignment algorithm configuration.
        /// Controls matching behavior, scores, and optimization parameters.
        /// </summary>
        public AlignmentConfiguration AlignmentConfig { get; set; } = new AlignmentConfiguration();

        /// <summary>
        /// Creates a new MediaOverlayGeneratorSettings from old AlignmentConfiguration (for backward compatibility).
        /// </summary>
        public static MediaOverlayGeneratorSettings FromAlingnerConfig(
            ITranscriptionScript script,
            AlignmentConfiguration oldConfig)
        {
            var newConfig = new AlignmentConfiguration
            {
                MicroJobFragmentThreshold = oldConfig.MicroJobFragmentThreshold,
                AnchorSearchDivisor = oldConfig.AnchorSearchDivisor,
                RequiredQuickExitScore = oldConfig.RequiredQuickExitScore,
                ValidAnchorScoreThreshold = oldConfig.ValidAnchorScoreThreshold,
                ExpansionDepth = oldConfig.ExpansionDepth,
                AnchorValidationExpansionDepth = oldConfig.AnchorValidationExpansionDepth,
                SentenceSearchBuffer = oldConfig.SentenceSearchBuffer,
                SearchWordRangeAdjustment = oldConfig.SearchWordRangeAdjustment,
                WindowLengthMultiplier = oldConfig.WindowLengthMultiplier,
                ExpansionPassScore = oldConfig.ExpansionPassScore,
                LowScoreDetectionThreshold = oldConfig.LowScoreDetectionThreshold,
                ScoreValidationRatio = oldConfig.ScoreValidationRatio,
                BackupStrategyScoreThreshold = oldConfig.BackupStrategyScoreThreshold,
                BackupScoreRequirement = oldConfig.BackupScoreRequirement,
                BackupResultDummyScore = oldConfig.BackupResultDummyScore,
                BackupGapTolerance = oldConfig.BackupGapTolerance,
                ScoreDropThresholdForEarlyExit = oldConfig.ScoreDropThresholdForEarlyExit,
                PunctuationBonusScore = oldConfig.PunctuationBonusScore,
                DefaultSegmentLength = oldConfig.DefaultSegmentLength,
            };

            return new MediaOverlayGeneratorSettings
            {
                TranscriptionScript = script,
                AlignmentConfig = newConfig
            };
        }
    }
}
