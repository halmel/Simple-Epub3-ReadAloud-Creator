using Epub3MediaOverlays.Core.MediaOverlayGeneration;
using Epub3MediaOverlays.Wpf.Views;
using Newtonsoft.Json;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Controls;
using Epub3MediaOverlays.Wpf.ViewModels;

namespace Epub3MediaOverlays.Wpf
{
    public partial class LogViewerWindow : Window
    {
        private LogViewModel _logViewModel;
        private AlignmentLogTree _logTree;

        public LogViewerWindow(string logFilePath)
        {
            InitializeComponent();

            try
            {
                _logTree = LoadLogTree(logFilePath);
                _logViewModel = new LogViewModel(_logTree);
                DataContext = _logViewModel;

                // Set up tree selection handler
                TreeViewControl.SelectedItemChanged += TreeViewControl_SelectedItemChanged;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading log file: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private AlignmentLogTree LoadLogTree(string path)
        {
            if (!File.Exists(path))
                throw new FileNotFoundException($"Log file not found: {path}");

            var json = File.ReadAllText(path);
            var logTree = JsonConvert.DeserializeObject<AlignmentLogTree>(json);

            if (logTree == null)
                throw new InvalidOperationException("Failed to deserialize log tree from JSON");

            return logTree;
        }

        private void TreeViewControl_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (e.NewValue is LogNodeViewModel selectedNode)
            {
                DisplayNodeDetails(selectedNode);
            }
        }

        private void DisplayNodeDetails(LogNodeViewModel node)
        {
            if (node.IsMicroJob)
            {
                // Show fragment alignment view
                var fragmentView = new FragmentAlignmentView();
                var fragmentViewModel = new FragmentAlignmentViewModel(
                    node.Node, 
                    _logTree.OriginalWordText, 
                    _logTree.OriginalFragmentText
                );
                fragmentView.DataContext = fragmentViewModel;
                DetailsPanel.Content = fragmentView;
            }
            else
            {
                // Show tree info view
                var treeInfoView = CreateTreeInfoView(node);
                DetailsPanel.Content = treeInfoView;
            }
        }

        private UIElement CreateTreeInfoView(LogNodeViewModel node)
        {
            var panel = new StackPanel { Margin = new Thickness(10) };

            // Title
            var title = new TextBlock
            {
                Text = "Node Details",
                FontSize = 14,
                FontWeight = FontWeights.Bold,
                Foreground = System.Windows.Media.Brushes.Black,
                Margin = new Thickness(0, 0, 0, 10)
            };
            panel.Children.Add(title);

            // Status
            var statusBlock = new TextBlock
            {
                Text = $"Status: {node.StatusLabel}",
                FontSize = 12,
                Margin = new Thickness(0, 5, 0, 0)
            };
            panel.Children.Add(statusBlock);

            // Coordinates
            var coordBlock = new TextBlock
            {
                Text = node.CoordinateSummary,
                FontSize = 11,
                Margin = new Thickness(0, 5, 0, 0),
                TextWrapping = TextWrapping.Wrap
            };
            panel.Children.Add(coordBlock);

            // Details
            var detailsBlock = new TextBlock
            {
                Text = $"Details: {node.DetailsSummary}",
                FontSize = 11,
                Margin = new Thickness(0, 5, 0, 0)
            };
            panel.Children.Add(detailsBlock);

            // Gaps (if any)
            if (node.HasGaps)
            {
                var gapsBlock = new TextBlock
                {
                    Text = $"Gaps: {node.GapSummary}",
                    FontSize = 11,
                    Margin = new Thickness(0, 5, 0, 0),
                    Foreground = System.Windows.Media.Brushes.Orange
                };
                panel.Children.Add(gapsBlock);
            }

            // Error (if failed)
            if (node.Node.Status == AlignmentStatus.Failed)
            {
                var errorBlock = new TextBlock
                {
                    Text = $"Error: {node.ErrorMessage}",
                    FontSize = 11,
                    Margin = new Thickness(0, 5, 0, 0),
                    Foreground = System.Windows.Media.Brushes.Red,
                    TextWrapping = TextWrapping.Wrap
                };
                panel.Children.Add(errorBlock);
            }

            // Node type
            var typeBlock = new TextBlock
            {
                Text = node.NodeTypeSummary,
                FontSize = 10,
                Margin = new Thickness(0, 10, 0, 0),
                Foreground = System.Windows.Media.Brushes.Gray,
                FontStyle = FontStyles.Italic
            };
            panel.Children.Add(typeBlock);

            return new ScrollViewer { Content = panel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        }

        private void DetailsPanel_Loaded(object sender, RoutedEventArgs e)
        {
            // Initialize with first node if available
            if (_logViewModel?.RootNodes.Count > 0)
            {
                DisplayNodeDetails(_logViewModel.RootNodes[0]);
            }
        }

        private void TreeView_SelectionChanged(object sender, MouseButtonEventArgs e)
        {
            // Force selection update
            if (TreeViewControl.SelectedItem is LogNodeViewModel selectedNode)
            {
                DisplayNodeDetails(selectedNode);
            }
        }
    }
}


