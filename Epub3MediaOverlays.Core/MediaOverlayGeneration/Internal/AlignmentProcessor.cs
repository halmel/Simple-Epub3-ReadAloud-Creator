using System.Diagnostics;
using Epub3MediaOverlays.Core.MediaOverlayGeneration.Models;

namespace Epub3MediaOverlays.Core.MediaOverlayGeneration.Internal
{
    /// <summary>
    /// Internal alignment processor that matches audio fragments to text segments.
    /// Implements the core alignment algorithm using fuzzy matching and heuristic anchoring.
    /// </summary>
    internal partial class AlignmentProcessor
    {
        public string WordPath { get; set; }
        public string LogPath { get; set; }
        public LogLevel MinLogLevel { get; set; } = LogLevel.Green;
        public AlignmentConfiguration Config { get; set; }

        public List<WordSegment> BookSegments { get; set; }
        public List<AudioFragment> TranscriptSegments { get; set; }

        public int? DebugBreakOnFragmentIndex { get; set; } = 225;
        public int? DebugBreakOnWordIndex { get; set; }
        public List<int> DebugBreakOnFragmentIndices { get; set; } = new List<int>();
        public List<int> DebugBreakOnWordIndices { get; set; } = new List<int>();
        public (int Start, int End)? DebugBreakOnFragmentRange { get; set; }
        public (int Start, int End)? DebugBreakOnWordRange { get; set; }

        public char[] words { get; private set; }
        public AlignmentMapper[] WordsMap { get; set; }
        public AlignmentMapper[] WordsSentences { get; set; }

        public char[] fragments { get; private set; }
        public AlignmentMapper[] FragmentsMap { get; set; }

        public AlignmentProcessor(ref List<WordSegment> bookSegments,
                                   ref List<AudioFragment> transcriptSegments,
                                   string wordPath, string logPath,
                                   AlignmentConfiguration config = null,
                                   IAlignmentLogger logger = null)
        {
            WordPath = wordPath;
            LogPath = logPath;
            MinLogLevel = LogLevel.Green;
            Config = config ?? new AlignmentConfiguration();
            BookSegments = bookSegments;
            TranscriptSegments = transcriptSegments;
            _logger = logger ?? new AlignmentLogger();

            InitializeAlignmentData();
            InitializeAlignmentLogger();
        }

        /// <summary>
        /// Checks if the current fragment and word indices match any debug breakpoint conditions.
        /// </summary>
        [Conditional("DEBUG")]
        public void CheckDebugBreakpoint(int fragmentIndex, int wordIndex)
        {
            if (DebugBreakOnFragmentIndex.HasValue && DebugBreakOnFragmentIndex.Value == fragmentIndex)
            {
                Debugger.Break();
                return;
            }

            if (DebugBreakOnWordIndex.HasValue && DebugBreakOnWordIndex.Value == wordIndex)
            {
                Debugger.Break();
                return;
            }

            if (DebugBreakOnFragmentIndices.Count > 0 && DebugBreakOnFragmentIndices.Contains(fragmentIndex))
            {
                Debugger.Break();
                return;
            }

            if (DebugBreakOnWordIndices.Count > 0 && DebugBreakOnWordIndices.Contains(wordIndex))
            {
                Debugger.Break();
                return;
            }

            if (DebugBreakOnFragmentRange.HasValue)
            {
                var range = DebugBreakOnFragmentRange.Value;
                if (fragmentIndex >= range.Start && fragmentIndex <= range.End)
                {
                    Debugger.Break();
                    return;
                }
            }

            if (DebugBreakOnWordRange.HasValue)
            {
                var range = DebugBreakOnWordRange.Value;
                if (wordIndex >= range.Start && wordIndex <= range.End)
                {
                    Debugger.Break();
                    return;
                }
            }
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
