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

    public class BookgroupList
    {
        public ObservableCollection<BookGroup> Tabs { get; } = new ObservableCollection<BookGroup>();

        private AppSettings _settings;
        private TabControl _tabControl;

        private BookGroup _currentGroup;
        public BookGroup CurrentGroup
        {
            get => _currentGroup;
             set
            {
                if (_currentGroup != value)
                {
                    _currentGroup = value;
                    // TODO: Notify if needed (INotifyPropertyChanged or event)
                }
            }
        }

        public BookgroupList(AppSettings settings, TabControl tabControl)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _tabControl = tabControl ?? throw new ArgumentNullException(nameof(tabControl));

            // Bind the TabControl ItemsSource to Tabs collection
            _tabControl.ItemsSource = Tabs;

            // Handle tab selection changed
            _tabControl.SelectionChanged += GroupTabs_SelectionChanged;

            // Load existing groups and books from disk
            LoadBooks(_settings);
        }
        public BookgroupList()
        {

        }

        private void GroupTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_tabControl.SelectedItem is BookGroup selectedGroup)
            {
                CurrentGroup = selectedGroup;
            }
        }

        public void LoadBooks(AppSettings settings)
        {
            var EbooksRoot = settings.EbooksPath;

            if (!Directory.Exists(EbooksRoot))
                return;

            // Clear existing groups but keep the collection (no clearing Tabs replaced)
            // Tabs.Clear();  // if you want to clear old groups uncomment this line

            var groupsOnDisk = new List<BookGroup>();

            // Each folder in EbooksRoot is a group folder
            foreach (var groupFolder in Directory.GetDirectories(EbooksRoot))
            {
                var groupName = Path.GetFileName(groupFolder);
                var group = new BookGroup
                {
                    Name = groupName,
                    Books = new ObservableCollection<Book>()
                };

                // Each folder inside the group folder is a book folder
                foreach (var bookFolder in Directory.GetDirectories(groupFolder))
                {
                    string title = Path.GetFileName(bookFolder);
                    string epubFolder = Path.Combine(bookFolder, "OriginalEpub");
                    string audioFolder = Path.Combine(bookFolder, "Audio");

                    string epubFile = Directory.Exists(epubFolder)
                        ? Directory.GetFiles(epubFolder, "*.epub").FirstOrDefault()
                        : null;

                    List<string> mp3Files = Directory.Exists(audioFolder)
                        ? Directory.GetFiles(audioFolder, "*.mp3").ToList()
                        : new List<string>();

                    if (epubFile != null)
                    {
                        bool isProcessed = File.Exists(Path.Combine(groupFolder, "ProcessedBooks", Path.GetFileName(epubFile)));

                        var book = new Book
                        {
                            Title = title,
                            EpubFile = epubFile,
                            Mp3Files = mp3Files,
                            FolderPath = bookFolder,
                            IsProssed = isProcessed,
                            Progress = isProcessed ? 100 : 0,
                            Status = isProcessed ? BookStatus.Completed : BookStatus.Idle
                        };

                        string expectedCoverPath = Path.Combine(bookFolder, "cover.jpg");
                        if (File.Exists(expectedCoverPath))
                        {
                            book.CoverPath = expectedCoverPath;
                        }
                        else
                        {
                            string? extractedCoverPath = EpubCoverExtractor.ExtractCoverImage(epubFile);
                            if (extractedCoverPath != null)
                            {
                                book.CoverPath = extractedCoverPath;
                            }
                        }

                        book.RefreshAlignmentLogStatus();
                        group.Books.Add(book);
                    }
                }

                // Sort books: unprocessed first
                var sortedBooks = group.Books.OrderBy(b => b.IsProssed).ToList();
                group.Books.Clear();
                foreach (var b in sortedBooks)
                    group.Books.Add(b);

                groupsOnDisk.Add(group);
            }

            // Now update Tabs collection with groupsOnDisk

            // Remove groups that no longer exist
            for (int i = Tabs.Count - 1; i >= 0; i--)
            {
                var existingGroup = Tabs[i];
                if (!groupsOnDisk.Any(g => g.Name == existingGroup.Name))
                {
                    Tabs.RemoveAt(i);
                }
            }

            // Add or update groups from disk
            foreach (var groupFromDisk in groupsOnDisk)
            {
                var existingGroup = Tabs.FirstOrDefault(g => g.Name == groupFromDisk.Name);
                if (existingGroup == null)
                {
                    // New group, add it
                    Tabs.Add(groupFromDisk);
                }
                else
                {
                    // Existing group, update books

                    // Remove books that no longer exist
                    for (int i = existingGroup.Books.Count - 1; i >= 0; i--)
                    {
                        var existingBook = existingGroup.Books[i];
                        if (!groupFromDisk.Books.Any(b => b.Title == existingBook.Title))
                        {
                            existingGroup.Books.RemoveAt(i);
                        }
                    }

                    // Add or update books
                    foreach (var bookFromDisk in groupFromDisk.Books)
                    {
                        var existingBook = existingGroup.Books.FirstOrDefault(b => b.Title == bookFromDisk.Title);
                        if (existingBook == null)
                        {
                            existingGroup.Books.Add(bookFromDisk);
                        }
                        else
                        {
                            existingBook.EpubFile = bookFromDisk.EpubFile;
                            existingBook.Mp3Files = bookFromDisk.Mp3Files;
                            existingBook.FolderPath = bookFromDisk.FolderPath;
                            existingBook.IsProssed = bookFromDisk.IsProssed;
                            existingBook.Progress = bookFromDisk.Progress;
                            existingBook.Status = bookFromDisk.Status;
                            existingBook.CoverPath = bookFromDisk.CoverPath;
                            existingBook.RefreshAlignmentLogStatus();
                        }
                    }

                    // Sort books inside the group
                    var sorted = existingGroup.Books.OrderBy(b => b.IsProssed).ToList();
                    for (int i = 0; i < sorted.Count; i++)
                    {
                        var sortedBook = sorted[i];
                        int currentIndex = existingGroup.Books.IndexOf(sortedBook);
                        if (currentIndex != i)
                        {
                            existingGroup.Books.Move(currentIndex, i);
                        }
                    }
                }
            }

            // If CurrentGroup is null or no longer exists, set it to first group
            if (CurrentGroup == null || !Tabs.Contains(CurrentGroup))
            {
                CurrentGroup = Tabs.FirstOrDefault();
            }
        }


        public void AddNewGroup(string groupName)
        {
            if (string.IsNullOrWhiteSpace(groupName))
                throw new ArgumentException("Group name cannot be empty.", nameof(groupName));

            if (Tabs.Any(g => g.Name.Equals(groupName, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException($"A group named '{groupName}' already exists.");

            var newGroup = new BookGroup(groupName);
            Tabs.Add(newGroup);
            CurrentGroup = newGroup;

            // Create folder structure on disk
            string groupFolderPath = Path.Combine(_settings.EbooksPath, groupName);
            Directory.CreateDirectory(groupFolderPath);
            Directory.CreateDirectory(Path.Combine(groupFolderPath, "ProcessedBooks"));

            // Optionally, trigger UI update or selection logic
            _tabControl.SelectedItem = newGroup;
        }

        public void RenameGroup(string oldName, string newName)
        {
            if (string.IsNullOrWhiteSpace(oldName) || string.IsNullOrWhiteSpace(newName))
                throw new ArgumentException("Group names cannot be empty.");

            if (oldName.Equals(newName, StringComparison.OrdinalIgnoreCase))
                return;

            if (!Tabs.Any(g => g.Name == oldName))
                throw new InvalidOperationException($"Group '{oldName}' does not exist.");

            if (Tabs.Any(g => g.Name.Equals(newName, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidOperationException($"A group named '{newName}' already exists.");

            string oldPath = Path.Combine(_settings.EbooksPath, oldName);
            string newPath = Path.Combine(_settings.EbooksPath, newName);

            if (!Directory.Exists(oldPath))
                throw new DirectoryNotFoundException($"Original group folder not found: {oldPath}");

            // Copy all contents recursively
            CopyDirectoryRecursive(oldPath, newPath);

            // Remove the old directory
            Directory.Delete(oldPath, recursive: true);

            // Update in-memory group
            var oldGroup = Tabs.First(g => g.Name == oldName);
            oldGroup.Name = newName;

            // Update current group if needed
            if (CurrentGroup == oldGroup)
                CurrentGroup = oldGroup;

            // Refresh tabs (force notify if UI binding needed)
            OnPropertyChanged(nameof(Tabs));
        }

        private void CopyDirectoryRecursive(string sourceDir, string targetDir)
        {
            Directory.CreateDirectory(targetDir);

            // Copy files
            foreach (var file in Directory.GetFiles(sourceDir))
            {
                string targetFilePath = Path.Combine(targetDir, Path.GetFileName(file));
                File.Copy(file, targetFilePath, overwrite: true);
            }

            // Copy subdirectories
            foreach (var directory in Directory.GetDirectories(sourceDir))
            {
                string targetSubDir = Path.Combine(targetDir, Path.GetFileName(directory));
                CopyDirectoryRecursive(directory, targetSubDir);
            }
        }


        public void SaveBooks(AppSettings settings)
        {
            if (CurrentGroup == null)
                return;

            foreach (var book in CurrentGroup.Books)
            {
                string bookFolder = Path.Combine(settings.EbooksPath, CurrentGroup.Name, book.Title);
                if (!Directory.Exists(bookFolder))
                {
                    SaveBookToDisk(book, CurrentGroup, settings);
                }
            }
        }
        public static string SanitizeFolderName(string folderName)
        {
            // Get invalid characters for file/folder names in Windows
            char[] invalidChars = Path.GetInvalidFileNameChars();

            // Replace invalid chars with underscore or remove them
            foreach (var c in invalidChars)
            {
                folderName = folderName.Replace(c, '_'); // Or string.Empty to remove
            }

            // Optionally trim whitespace
            return folderName.Trim();
        }

        private void SaveBookToDisk(Book book, BookGroup currentGroup, AppSettings settings)
        {
            book.Title = SanitizeFolderName(book.Title);
            int maxLength = 100; 
            if (book.Title.Length > maxLength)
                book.Title = book.Title.Substring(0, maxLength);

            string ebooksRoot = settings.EbooksPath;

            string bookFolder = Path.Combine(ebooksRoot, currentGroup.Name, book.Title);
            string epubFolder = Path.Combine(bookFolder, "OriginalEpub");
            string processedFolder = Path.Combine(ebooksRoot, currentGroup.Name, "ProcessedBooks");
            string audioFolder = Path.Combine(bookFolder, "Audio");

            Directory.CreateDirectory(bookFolder);
            Directory.CreateDirectory(epubFolder);
            Directory.CreateDirectory(processedFolder);
            Directory.CreateDirectory(audioFolder);

            // Copy EPUB file
            if (!string.IsNullOrEmpty(book.EpubFile) && File.Exists(book.EpubFile))
            {
                string epubFileName = Path.GetFileName(book.EpubFile);
                string epubTarget = Path.Combine(epubFolder, epubFileName);
                File.Copy(book.EpubFile, epubTarget, overwrite: true);
                book.EpubFile = epubTarget;
            }

            // Extract and save cover image
            string? coverPath = EpubCoverExtractor.ExtractCoverImage(book.EpubFile);
            if (coverPath != null)
            {
                book.CoverPath = coverPath;
            }

            // Copy MP3 files
            List<string> copiedMp3s = new List<string>();
            foreach (var mp3 in book.Mp3Files ?? Enumerable.Empty<string>())
            {
                if (File.Exists(mp3))
                {
                    string mp3Name = Path.GetFileName(mp3);
                    string targetMp3 = Path.Combine(audioFolder, mp3Name);
                    File.Copy(mp3, targetMp3, overwrite: true);
                    copiedMp3s.Add(targetMp3);
                }
            }

            book.Mp3Files = copiedMp3s;
        }


        public void AddBook(Book book, AppSettings settings)
        {
            if (book == null) throw new ArgumentNullException(nameof(book));
            if (CurrentGroup == null)
                throw new InvalidOperationException("No current group selected.");

            CurrentGroup.Books.Add(book);
            SaveBookToDisk(book, CurrentGroup, settings);
        }


        public void EditBook(Book originalBook, string newTitle, string newEpubPath, List<string> newMp3Paths, AppSettings settings)
        {
            if (originalBook == null)
                throw new ArgumentNullException(nameof(originalBook));
            if (CurrentGroup == null)
                throw new InvalidOperationException("No current group selected.");

            bool titleChanged = !string.Equals(originalBook.Title, newTitle, StringComparison.OrdinalIgnoreCase);
            bool epubChanged = !string.Equals(originalBook.EpubFile, newEpubPath, StringComparison.OrdinalIgnoreCase);
            bool mp3Changed = !Enumerable.SequenceEqual(originalBook.Mp3Files ?? new(), newMp3Paths ?? new());

            if (!titleChanged && !epubChanged && !mp3Changed)
                return; // Nothing to update

            if (epubChanged || mp3Changed)
            {
                // 1. Copy new files to temp book
                string tempFolder = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
                Directory.CreateDirectory(tempFolder);

                string tempEpub = null;
                List<string> tempMp3s = new();

                if (!string.IsNullOrEmpty(newEpubPath) && File.Exists(newEpubPath))
                {
                    string epubTarget = Path.Combine(tempFolder, Path.GetFileName(newEpubPath));
                    File.Copy(newEpubPath, epubTarget, overwrite: true);
                    tempEpub = epubTarget;
                }

                foreach (var mp3 in newMp3Paths ?? new())
                {
                    if (File.Exists(mp3))
                    {
                        string mp3Target = Path.Combine(tempFolder, Path.GetFileName(mp3));
                        File.Copy(mp3, mp3Target, overwrite: true);
                        tempMp3s.Add(mp3Target);
                    }
                }

                // 2. Remove old book and its processed files
                RemoveBook(originalBook, settings);

                // 3. Create new book and save
                var newBook = new Book
                {
                    Title = newTitle,
                    EpubFile = tempEpub,
                    Mp3Files = tempMp3s
                };

                CurrentGroup.Books.Add(newBook);
                SaveBookToDisk(newBook, CurrentGroup, settings);
            }
            else if (titleChanged)
            {
                // Only title changed: move folder
                string groupFolder = Path.Combine(settings.EbooksPath, CurrentGroup.Name);
                string oldFolder = Path.Combine(groupFolder, originalBook.Title);
                string newFolder = Path.Combine(groupFolder, newTitle);

                if (Directory.Exists(oldFolder))
                {
                    Directory.Move(oldFolder, newFolder);
                    originalBook.Title = newTitle;
                    originalBook.FolderPath = newFolder;

                    originalBook.OnPropertyChanged(nameof(Book.Title));
                    originalBook.OnPropertyChanged(nameof(Book.FolderPath));
                }
            }
        }

        public void RemoveBook(Book book, AppSettings settings)
        {
            if (book == null)
                throw new ArgumentNullException(nameof(book));

            if (CurrentGroup == null)
                throw new InvalidOperationException("No current group selected.");

            string groupFolder = Path.Combine(settings.EbooksPath, CurrentGroup.Name);
            string bookFolder = Path.Combine(groupFolder, book.Title);

            // Delete book folder
            if (Directory.Exists(bookFolder))
            {
                Directory.Delete(bookFolder, recursive: true);
            }

            // Delete processed EPUB if present
            if (book.IsProssed && !string.IsNullOrEmpty(book.EpubFile))
            {
                string processedEpub = Path.Combine(groupFolder, "ProcessedBooks", Path.GetFileName(book.EpubFile));
                if (File.Exists(processedEpub))
                {
                    File.Delete(processedEpub);
                }
            }

            CurrentGroup.Books.Remove(book);
        }
        public void MoveBookToGroup(Book book, BookGroup targetGroup, AppSettings settings)
        {
            if (book == null) throw new ArgumentNullException(nameof(book));
            if (targetGroup == null) throw new ArgumentNullException(nameof(targetGroup));
            if (settings == null) throw new ArgumentNullException(nameof(settings));

            var sourceGroup = Tabs.FirstOrDefault(g => g.Books.Contains(book));
            if (sourceGroup == null)
                throw new InvalidOperationException("Book does not belong to any known group.");

            if (sourceGroup == targetGroup)
                return; // No move needed

            string sourceBookFolder = Path.Combine(settings.EbooksPath, sourceGroup.Name, book.Title);
            string targetGroupFolder = Path.Combine(settings.EbooksPath, targetGroup.Name);
            string targetBookFolder = Path.Combine(targetGroupFolder, book.Title);

            if (!Directory.Exists(sourceBookFolder))
                throw new DirectoryNotFoundException($"Source book folder not found: {sourceBookFolder}");

            if (Directory.Exists(targetBookFolder))
                throw new IOException($"Target book folder already exists: {targetBookFolder}");

            // Copy book folder to temp folder
            string tempFolder = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
            Directory.CreateDirectory(tempFolder);
            CopyDirectoryRecursive(sourceBookFolder, tempFolder);

            // Remove original book folder
            Directory.Delete(sourceBookFolder, recursive: true);

            // Move temp folder to target group folder
            Directory.Move(tempFolder, targetBookFolder);

            // Handle processed EPUB move if the book is processed
            if (book.IsProssed && !string.IsNullOrEmpty(book.EpubFile))
            {
                string processedSourceFolder = Path.Combine(settings.EbooksPath, sourceGroup.Name, "ProcessedBooks");
                string processedTargetFolder = Path.Combine(settings.EbooksPath, targetGroup.Name, "ProcessedBooks");

                Directory.CreateDirectory(processedTargetFolder);

                string processedEpubName = Path.GetFileName(book.EpubFile);
                string processedSourceFile = Path.Combine(processedSourceFolder, processedEpubName);
                string processedTargetFile = Path.Combine(processedTargetFolder, processedEpubName);

                if (File.Exists(processedSourceFile))
                {
                    File.Copy(processedSourceFile, processedTargetFile, overwrite: true);
                    File.Delete(processedSourceFile);
                }
            }

            // Update book's FolderPath and move between groups in-memory (on UI thread)
            book.FolderPath = targetBookFolder;

            Application.Current.Dispatcher.Invoke(() =>
            {
                sourceGroup.Books.Remove(book);
                targetGroup.Books.Add(book);
            });
        }


        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

    }

    public class BookGroup
    {
        public string Name { get; set; }
        public ObservableCollection<Book> Books { get; set; } = new ObservableCollection<Book>();

        public BookGroup(string name)
        {
            Name = name;
        }

        public BookGroup() : this("Default") { }
    }


    public class Book : INotifyPropertyChanged
    {
        private bool hasAlignmentLog;
        public bool HasAlignmentLog
        {
            get => hasAlignmentLog;
            private set
            {
                if (hasAlignmentLog != value)
                {
                    hasAlignmentLog = value;
                    OnPropertyChanged(nameof(HasAlignmentLog));
                }
            }
        }

        public void RefreshAlignmentLogStatus()
        {
            string logPath = Path.Combine(FolderPath, "OriginalEpub", "AlingmentLog.json");
            HasAlignmentLog = File.Exists(logPath);
        }


        public enum BookStatus
        {
            Idle,
            WaitingInQueue,
            Running,
            Completed
        }


        private BookStatus status = BookStatus.Idle;
        public BookStatus Status
        {
            get => status;
            set
            {
                if (status != value)
                {
                    status = value;
                    OnPropertyChanged(nameof(Status));
                    OnPropertyChanged(nameof(StatusText));
                }
            }
        }

        private int queuePosition = -1;
        public int QueuePosition
        {
            get => queuePosition;
            set
            {
                if (queuePosition != value)
                {
                    queuePosition = value;
                    OnPropertyChanged(nameof(QueuePosition));
                    OnPropertyChanged(nameof(StatusText));
                }
            }
        }

        public string StatusText =>
            Status == BookStatus.WaitingInQueue
            ? $"Status: Waiting in queue (#{QueuePosition + 1})"
            : $"Status: {Status}";









        public string Title { get; set; }
        public string EpubFile { get; set; }
        public List<string> Mp3Files { get; set; }
        public string FolderPath { get; set; }
        public string CoverPath { get; set; }

        public bool IsProssed { get; set; }

        public string EpubFileName => Path.GetFileName(EpubFile);

        public List<string> Mp3FilesNames =>
            Mp3Files.Select(mp3 => Path.GetFileName(mp3)).ToList();

        private int progress;
        public int Progress
        {
            get => progress;
            set
            {
                if (progress != value)
                {
                    progress = value;
                    OnPropertyChanged(nameof(Progress));
                }
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public virtual void OnPropertyChanged(string propertyName)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            });
        }
        private bool isSelected;
        public bool IsSelected
        {
            get => isSelected;
            set
            {
                if (isSelected != value)
                {
                    isSelected = value;
                    OnPropertyChanged(nameof(IsSelected));
                }
            }
        }



    }
}
