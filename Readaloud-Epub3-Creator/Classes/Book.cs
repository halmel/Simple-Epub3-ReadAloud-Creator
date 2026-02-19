using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace Readaloud_Epub3_Creator
{
    using System;
    using System.Collections.ObjectModel;
    using System.IO;
    using System.Linq;
    using System.Runtime.CompilerServices;
    using System.Windows.Controls;
    using static Readaloud_Epub3_Creator.Book;

    public enum BookStatus
    {
        Idle,
        WaitingInQueue,
        Running,
        Completed
    }


    public class Book : INotifyPropertyChanged
    {
        private BookData _data;
        public BookData Data => _data;

        // Metadata
        private string _title;
        public string Title
        {
            get => _title;
            set { _title = value; OnPropertyChanged(nameof(Title)); }
        }

        // Status & Progress
        private BookStatus _status = BookStatus.Idle;
        public BookStatus Status
        {
            get => _status;
            set { _status = value; OnPropertyChanged(nameof(Status)); OnPropertyChanged(nameof(StatusText)); }
        }

        private int _progress;
        public int Progress
        {
            get => _progress;
            set { _progress = value; OnPropertyChanged(nameof(Progress)); }
        }

        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set { _isSelected = value; OnPropertyChanged(nameof(IsSelected)); }
        }

        public bool IsProcessed { get; set; }
        public bool HasAlignmentLog => Data.AlignmentLogPath != null;
        public string StatusText => Status == BookStatus.WaitingInQueue ? $"Waiting (#{QueuePosition + 1})" : Status.ToString();
        public int QueuePosition { get; set; }

        public Book(string title, string folderPath)
        {
            _title = title;
            _data = new BookData(folderPath);
        }

        // UI helper to refresh file-based flags
        public void RefreshDataState()
        {
            OnPropertyChanged(nameof(HasAlignmentLog));
            OnPropertyChanged(nameof(Data));
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected virtual void OnPropertyChanged(string name) =>
            Application.Current.Dispatcher.Invoke(() => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name)));
    }


    public class BookData
    {
        public string RootFolder { get; }

        // Sub-folder constants to avoid duplication
        private const string EpubDir = "OriginalEpub";
        private const string AudioDir = "Audio";
        private const string LogName = "AlingmentLog.json";
        private const string CoverName = "cover.jpg";

        public BookData(string rootFolder)
        {
            RootFolder = rootFolder;
        }

        // Nullable properties: Only return path if file actually exists
        public string? EpubPath => Directory.Exists(EpubPathDir)
            ? Directory.GetFiles(EpubPathDir, "*.epub").FirstOrDefault()
            : null;

        public List<string> Mp3Paths => Directory.Exists(AudioPathDir)
            ? Directory.GetFiles(AudioPathDir, "*.mp3").ToList()
            : new List<string>();

        public string? CoverPath
        {
            get
            {
                string localCover = Path.Combine(RootFolder, CoverName);
                if (File.Exists(localCover)) return localCover;

                // Fallback: Try to extract from Epub if path is null
                return EpubPath != null ? EpubCoverExtractor.ExtractCoverImage(EpubPath) : null;
            }
        }

        public string? AlignmentLogPath
        {
            get
            {
                string path = Path.Combine(EpubPathDir, LogName);
                return File.Exists(path) ? path : null;
            }
        }

        // Helper paths for internal use
        public string EpubPathDir => Path.Combine(RootFolder, EpubDir);
        public string AudioPathDir => Path.Combine(RootFolder, AudioDir);

        public void EnsureDirectories()
        {
            Directory.CreateDirectory(RootFolder);
            Directory.CreateDirectory(EpubPathDir);
            Directory.CreateDirectory(AudioPathDir);
        }
        // The folder where the final generated EPUB will be moved
        public string GlobalProcessedFolder => Path.GetFullPath(Path.Combine(RootFolder, "..", "ProcessedBooks"));

        // The full path for the final EPUB file
        public string FinalEpubOutputPath => EpubPath != null
            ? Path.Combine(GlobalProcessedFolder, Path.GetFileName(EpubPath))
            : string.Empty;

        // Temporary workspace for building the new EPUB structure
        public string TempProcessingFolder => Path.Combine(RootFolder, "ProcessedEpub");

        // Path for the transcription cache
        public string TranscriptionJsonPath => Path.Combine(AudioPathDir, "transcriptions.json");

        // Path for the alignment cache
        public string WordsJsonPath => Path.Combine(EpubPathDir, "Words.json");
    }
}
