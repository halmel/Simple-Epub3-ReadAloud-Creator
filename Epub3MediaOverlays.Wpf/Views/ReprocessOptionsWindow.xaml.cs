using Epub3MediaOverlays.Core;
using System;
using System.IO;
using System.Windows;


namespace Epub3MediaOverlays.Wpf
{
    public partial class ReprocessOptionsWindow : Window
    {
        private readonly BookData _bookData;

        public bool DeleteTranscriptCache { get; private set; }
        public bool DeleteAlignmentCache { get; private set; }
        public bool DeleteFinalEpubFile { get; private set; }

        public ReprocessOptionsWindow(BookData bookData)
        {
            InitializeComponent();
            _bookData = bookData;
            InitializeFileStatus();
        }

        private void InitializeFileStatus()
        {
            // Transcript
            string transcriptPath = _bookData.TranscriptionJsonPath;
            bool transcriptExists = File.Exists(transcriptPath);
            TranscriptPath.Text = transcriptPath;
            TranscriptStatus.Text = transcriptExists ? "[EXISTS]" : "[NOT FOUND]";
            TranscriptStatus.Foreground = transcriptExists ? System.Windows.Media.Brushes.Green : System.Windows.Media.Brushes.Red;
            DeleteTranscript.IsEnabled = transcriptExists;
            DeleteTranscript.IsChecked = transcriptExists;

            // Alignment
            string alignmentPath = _bookData.WordsJsonPath;
            string logPath = _bookData.AlignmentLogPath;
            bool alignmentExists = File.Exists(alignmentPath) || File.Exists(logPath);
            AlignmentPath.Text = $"Words: {alignmentPath}\nLog: {logPath}";
            AlignmentStatus.Text = alignmentExists ? "[EXISTS]" : "[NOT FOUND]";
            AlignmentStatus.Foreground = alignmentExists ? System.Windows.Media.Brushes.Green : System.Windows.Media.Brushes.Red;
            DeleteAlignment.IsEnabled = alignmentExists;
            DeleteAlignment.IsChecked = alignmentExists;

            // Final EPUB
            string finalEpubPath = _bookData.FinalEpubOutputPath;
            bool finalEpubExists = !string.IsNullOrEmpty(finalEpubPath) && File.Exists(finalEpubPath);
            FinalEpubPath.Text = finalEpubPath;
            FinalEpubStatus.Text = finalEpubExists ? "[EXISTS]" : "[NOT FOUND]";
            FinalEpubStatus.Foreground = finalEpubExists ? System.Windows.Media.Brushes.Green : System.Windows.Media.Brushes.Red;
            DeleteFinalEpub.IsEnabled = finalEpubExists;
            DeleteFinalEpub.IsChecked = false;

            // Show warning if nothing can be deleted
            if (!transcriptExists && !alignmentExists && !finalEpubExists)
            {
                WarningMessage.Text = "No cacheable files found for this book. All processing data has already been deleted.";
                WarningMessage.Visibility = Visibility.Visible;
                ReprocessButton.IsEnabled = false;
            }
        }

        private void Reprocess_Click(object sender, RoutedEventArgs e)
        {
            DeleteTranscriptCache = DeleteTranscript.IsChecked == true;
            DeleteAlignmentCache = DeleteAlignment.IsChecked == true;
            DeleteFinalEpubFile = DeleteFinalEpub.IsChecked == true;

            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}



