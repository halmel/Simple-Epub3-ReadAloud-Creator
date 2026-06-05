using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using Epub3MediaOverlays.Core.MediaOverlayGeneration;

namespace Epub3MediaOverlays.Wpf.ViewModels
{
    /// <summary>
    /// View model for displaying a hierarchical alignment log tree in WPF.
    /// Converts the flat AlignmentLogNode tree into a UI-friendly nested structure.
    /// </summary>
    public class LogViewModel
    {
        public AlignmentLogTree LogTree { get; set; }
        public ObservableCollection<LogNodeViewModel> RootNodes { get; set; }

        public LogViewModel(AlignmentLogTree logTree)
        {
            LogTree = logTree;
            RootNodes = new ObservableCollection<LogNodeViewModel>();

            if (logTree?.RootNode != null)
            {
                RootNodes.Add(new LogNodeViewModel(logTree.RootNode));
            }
        }
    }

    /// <summary>
    /// A UI-friendly view model for a single AlignmentLogNode.
    /// Handles child node expansion, status visualization, and coordinate display.
    /// Implements INotifyPropertyChanged for proper WPF binding support.
    /// </summary>
    public class LogNodeViewModel : INotifyPropertyChanged
    {
        public AlignmentLogNode Node { get; }
        public ObservableCollection<LogNodeViewModel> Children { get; set; }

        private bool _isExpanded;
        public bool IsExpanded
        {
            get => _isExpanded;
            set
            {
                if (_isExpanded != value)
                {
                    _isExpanded = value;
                    OnPropertyChanged();

                    if (value && Children.Count == 0 && Node.SubJobs.Count > 0)
                    {
                        // Lazy-load children when first expanded
                        foreach (var child in Node.SubJobs)
                        {
                            Children.Add(new LogNodeViewModel(child));
                        }
                    }
                }
            }
        }

        public LogNodeViewModel(AlignmentLogNode node)
        {
            Node = node;
            Children = new ObservableCollection<LogNodeViewModel>();
        }

        /// <summary>Gets a human-readable status label with color coding.</summary>
        public string StatusLabel => Node.Status switch
        {
            AlignmentStatus.Success => "✓ Success",
            AlignmentStatus.Partial => "⚠ Partial",
            AlignmentStatus.Failed => "✗ Failed",
            AlignmentStatus.Split => "↓ Split",
            _ => "Unknown"
        };

        /// <summary>Gets the color for the status.</summary>
        public Brush StatusColor => Node.Status switch
        {
            AlignmentStatus.Success => Brushes.Green,
            AlignmentStatus.Partial => Brushes.Orange,
            AlignmentStatus.Failed => Brushes.Red,
            AlignmentStatus.Split => Brushes.LightBlue,
            _ => Brushes.Gray
        };

        /// <summary>Gets a coordinate summary for display (e.g., "Words: 0-100, Fragments: 0-50").</summary>
        public string CoordinateSummary =>
            $"Words: {Node.WordStartIndex}-{Node.WordEndIndex} | Fragments: {Node.FragmentStartIndex}-{Node.FragmentEndIndex}";

        /// <summary>Gets the word range size.</summary>
        public int WordRangeSize => Node.WordEndIndex - Node.WordStartIndex + 1;

        /// <summary>Gets the fragment range size.</summary>
        public int FragmentRangeSize => Node.FragmentEndIndex - Node.FragmentStartIndex + 1;

        /// <summary>Gets a formatted details summary.</summary>
        public string DetailsSummary => $"Words: {WordRangeSize} | Fragments: {FragmentRangeSize}";

        /// <summary>Gets the total number of word gaps in this node.</summary>
        public int WordGapCount => Node.WordGaps.Count;

        /// <summary>Gets the total number of fragment gaps in this node.</summary>
        public int FragmentGapCount => Node.FragmentGaps.Count;

        /// <summary>Gets whether this node has any gaps.</summary>
        public bool HasGaps => Node.WordGaps.Count > 0 || Node.FragmentGaps.Count > 0;

        /// <summary>Gets a summary of gaps (e.g., "2 word gaps, 1 fragment gap").</summary>
        public string GapSummary
        {
            get
            {
                var parts = new System.Collections.Generic.List<string>();
                if (Node.WordGaps.Count > 0)
                    parts.Add($"{Node.WordGaps.Count} word gap{(Node.WordGaps.Count != 1 ? "s" : "")}");
                if (Node.FragmentGaps.Count > 0)
                    parts.Add($"{Node.FragmentGaps.Count} fragment gap{(Node.FragmentGaps.Count != 1 ? "s" : "")}");

                return parts.Count > 0 ? string.Join(", ", parts) : "No gaps";
            }
        }

        /// <summary>Gets the error message if the node failed.</summary>
        public string ErrorMessage => Node.ErrorMessage ?? "No error";

        /// <summary>Gets whether this node is a leaf (no sub-jobs).</summary>
        public bool IsLeaf => Node.SubJobs.Count == 0;

        /// <summary>Gets whether this node is a micro-job with fragment results.</summary>
        public bool IsMicroJob => Node.IsMicroJob;

        /// <summary>Gets the view type: "Tree" for hierarchical nodes, "Fragment" for micro-jobs with results.</summary>
        public string ViewType => IsMicroJob ? "Fragment" : "Tree";

        /// <summary>Gets the total time spent on this node and its descendants.</summary>
        public string NodeTypeSummary
        {
            get
            {
                if (Node.HasSubJobs)
                    return $"Split into {Node.SubJobs.Count} sub-jobs";
                return "Leaf node";
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string propertyName = "")
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
