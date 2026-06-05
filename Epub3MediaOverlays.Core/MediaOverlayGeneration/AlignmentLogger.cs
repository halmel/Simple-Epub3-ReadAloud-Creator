using System;
using System.Collections.Generic;
using System.Linq;

namespace Epub3MediaOverlays.Core.MediaOverlayGeneration
{
    /// <summary>
    /// Represents the status of an alignment operation for a job or sub-job.
    /// </summary>
    public enum AlignmentStatus
    {
        /// <summary>Perfectly aligned without gaps.</summary>
        Success,

        /// <summary>Aligned but with some gaps generated.</summary>
        Partial,

        /// <summary>Complete failure; entire range marked as a gap.</summary>
        Failed,

        /// <summary>Split into sub-jobs for recursive processing.</summary>
        Split,

        /// <summary>Represents a gap entry - unmatched words or fragments.</summary>
        Gap
    }

    /// <summary>
    /// Represents a single fragment's alignment result within a micro-job.
    /// Captures all details about how a fragment was matched to words.
    /// </summary>
    public class FragmentAlignmentResult
    {
        /// <summary>Index of the fragment in the transcript.</summary>
        public int FragmentIndex { get; set; }

        /// <summary>Start index of matched words (inclusive).</summary>
        public int WordStartIndex { get; set; }

        /// <summary>End index of matched words (inclusive).</summary>
        public int WordEndIndex { get; set; }

        /// <summary>Number of words matched to this fragment.</summary>
        public int MatchedWordCount => WordEndIndex - WordStartIndex + 1;

        /// <summary>Fuzzy match confidence score (0-100).</summary>
        public int ConfidenceScore { get; set; }

        /// <summary>Status of this fragment alignment.</summary>
        public AlignmentStatus Status { get; set; }

        /// <summary>Error message if alignment failed.</summary>
        public string ErrorMessage { get; set; }

        /// <summary>The original fragment text (for reference).</summary>
        public string FragmentText { get; set; }

        /// <summary>The matched word text (for reference).</summary>
        public string MatchedWordsText { get; set; }

        /// <summary>Whether this alignment created gaps.</summary>
        public bool HasGaps { get; set; }

        /// <summary>Gap details if any.</summary>
        public List<WordGap> WordGapDetails { get; set; } = new();
    }

    /// <summary>
    /// Represents a gap in word alignment (missing or misaligned section of the book text).
    /// </summary>
    public class WordGap
    {
        public int StartWordIndex { get; set; }
        public int EndWordIndex { get; set; }

        public int Length => EndWordIndex - StartWordIndex + 1;
    }

    /// <summary>
    /// Represents a gap in fragment alignment (missing or misaligned section of the transcript).
    /// </summary>
    public class FragmentGap
    {
        public int StartFragmentIndex { get; set; }
        public int EndFragmentIndex { get; set; }

        public int Length => EndFragmentIndex - StartFragmentIndex + 1;
    }

    /// <summary>
    /// A single node in the alignment job tree, representing one alignment operation.
    /// Captures coordinates, status, and any sub-jobs or gaps.
    /// </summary>
    public class AlignmentLogNode
    {
        /// <summary>Unique identifier for this node.</summary>
        public string JobId { get; set; } = Guid.NewGuid().ToString("N");

        /// <summary>Word range processed by this job.</summary>
        public int WordStartIndex { get; set; }
        public int WordEndIndex { get; set; }

        /// <summary>Fragment range processed by this job.</summary>
        public int FragmentStartIndex { get; set; }
        public int FragmentEndIndex { get; set; }

        /// <summary>Current status of this alignment job.</summary>
        public AlignmentStatus Status { get; set; }

        /// <summary>Error message, populated only if Status is Failed.</summary>
        public string ErrorMessage { get; set; }

        /// <summary>Word gaps created by this job (if any).</summary>
        public List<WordGap> WordGaps { get; set; } = new();

        /// <summary>Fragment gaps created by this job (if any).</summary>
        public List<FragmentGap> FragmentGaps { get; set; } = new();

        /// <summary>Child nodes representing sub-jobs (if this job was split).</summary>
        public List<AlignmentLogNode> SubJobs { get; set; } = new();

        /// <summary>Fragment-level alignment results (only for leaf/micro-job nodes).</summary>
        public List<FragmentAlignmentResult> FragmentResults { get; set; } = new();

        /// <summary>Returns true if this node has any child jobs.</summary>
        public bool HasSubJobs => SubJobs.Count > 0;

        /// <summary>Returns true if this is a leaf node with fragment results.</summary>
        public bool IsMicroJob => FragmentResults.Count > 0;

        /// <summary>Returns the total number of descendant nodes (including self).</summary>
        public int DescendantCount => 1 + SubJobs.Sum(n => n.DescendantCount);
    }

    /// <summary>
    /// Container for the entire alignment log tree, including metadata about the original strings.
    /// This is the top-level object that gets serialized to JSON.
    /// </summary>
    public class AlignmentLogTree
    {
        /// <summary>Root node representing the initial full-range job.</summary>
        public AlignmentLogNode RootNode { get; set; }

        /// <summary>The original book text (word-space-separated).</summary>
        public string OriginalWordText { get; set; }

        /// <summary>The original transcript text (normalized).</summary>
        public string OriginalFragmentText { get; set; }

        /// <summary>Timestamp when alignment started.</summary>
        public DateTime StartTime { get; set; } = DateTime.UtcNow;

        /// <summary>Timestamp when alignment completed.</summary>
        public DateTime EndTime { get; set; }

        /// <summary>Total duration of the alignment process.</summary>
        public TimeSpan Duration => EndTime - StartTime;

        /// <summary>Summary of all gaps found during the entire process.</summary>
        public List<WordGap> AllWordGaps { get; set; } = new();
        public List<FragmentGap> AllFragmentGaps { get; set; } = new();

        /// <summary>Gets the total count of nodes in the tree.</summary>
        public int TotalNodeCount => RootNode?.DescendantCount ?? 0;
    }

    /// <summary>
    /// Interface for logging alignment operations. Allows dependency injection of the logger.
    /// </summary>
    public interface IAlignmentLogger
    {
        /// <summary>Initialize the logger with a root job to begin tracking.</summary>
        void Initialize(int wordStartIndex, int wordEndIndex, int fragmentStartIndex, int fragmentEndIndex, 
                        string originalWordText, string originalFragmentText);

        /// <summary>Create a child node under the current context for a sub-job.</summary>
        AlignmentLogNode CreateSubJob(int wordStartIndex, int wordEndIndex, 
                                      int fragmentStartIndex, int fragmentEndIndex);

        /// <summary>Mark a job as successfully aligned (no gaps).</summary>
        void LogSuccess(AlignmentLogNode node);

        /// <summary>Mark a job as partially aligned with gaps.</summary>
        void LogPartial(AlignmentLogNode node, List<WordGap> wordGaps, List<FragmentGap> fragmentGaps);

        /// <summary>Mark a job as failed.</summary>
        void LogFailed(AlignmentLogNode node, string errorMessage);

        /// <summary>Mark a job as split into sub-jobs.</summary>
        void LogSplit(AlignmentLogNode node);

        /// <summary>Log a fragment alignment result for a micro-job node.</summary>
        void LogFragmentResult(AlignmentLogNode node, FragmentAlignmentResult result);

        /// <summary>Finalize the log tree and prepare for serialization.</summary>
        AlignmentLogTree Finalize();
    }

    /// <summary>
    /// Default implementation of IAlignmentLogger that builds a hierarchical job tree.
    /// </summary>
    public class AlignmentLogger : IAlignmentLogger
    {
        private AlignmentLogTree _tree;
        private Stack<AlignmentLogNode> _nodeStack;

        public void Initialize(int wordStartIndex, int wordEndIndex, int fragmentStartIndex, int fragmentEndIndex,
                               string originalWordText, string originalFragmentText)
        {
            _tree = new AlignmentLogTree
            {
                OriginalWordText = originalWordText,
                OriginalFragmentText = originalFragmentText,
                RootNode = new AlignmentLogNode
                {
                    WordStartIndex = wordStartIndex,
                    WordEndIndex = wordEndIndex,
                    FragmentStartIndex = fragmentStartIndex,
                    FragmentEndIndex = fragmentEndIndex
                }
            };
            _nodeStack = new Stack<AlignmentLogNode>();
            _nodeStack.Push(_tree.RootNode);
        }

        public AlignmentLogNode CreateSubJob(int wordStartIndex, int wordEndIndex,
                                             int fragmentStartIndex, int fragmentEndIndex)
        {
            var currentNode = _nodeStack.Peek();
            var subJob = new AlignmentLogNode
            {
                WordStartIndex = wordStartIndex,
                WordEndIndex = wordEndIndex,
                FragmentStartIndex = fragmentStartIndex,
                FragmentEndIndex = fragmentEndIndex
            };
            currentNode.SubJobs.Add(subJob);
            return subJob;
        }

        public void LogSuccess(AlignmentLogNode node)
        {
            node.Status = AlignmentStatus.Success;
        }

        public void LogPartial(AlignmentLogNode node, List<WordGap> wordGaps, List<FragmentGap> fragmentGaps)
        {
            node.Status = AlignmentStatus.Partial;
            if (wordGaps != null)
            {
                node.WordGaps.AddRange(wordGaps);
                _tree.AllWordGaps.AddRange(wordGaps);
            }
            if (fragmentGaps != null)
            {
                node.FragmentGaps.AddRange(fragmentGaps);
                _tree.AllFragmentGaps.AddRange(fragmentGaps);
            }
        }

        public void LogFailed(AlignmentLogNode node, string errorMessage)
        {
            node.Status = AlignmentStatus.Failed;
            node.ErrorMessage = errorMessage;
        }

        public void LogSplit(AlignmentLogNode node)
        {
            node.Status = AlignmentStatus.Split;
        }

        public void LogFragmentResult(AlignmentLogNode node, FragmentAlignmentResult result)
        {
            node.FragmentResults.Add(result);
        }

        public AlignmentLogTree Finalize()
        {
            _tree.EndTime = DateTime.UtcNow;
            return _tree;
        }
    }
}
