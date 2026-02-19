using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Controls;

namespace Readaloud_Epub3_Creator
{
    public class BookgroupList : INotifyPropertyChanged
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
                    OnPropertyChanged(nameof(CurrentGroup));
                }
            }
        }

        public BookgroupList(AppSettings settings, TabControl tabControl)
        {
            _settings = settings ?? throw new ArgumentNullException(nameof(settings));
            _tabControl = tabControl ?? throw new ArgumentNullException(nameof(tabControl));
            _tabControl.ItemsSource = Tabs;
            _tabControl.SelectionChanged += GroupTabs_SelectionChanged;

            LoadBooks(_settings);
        }

        private void GroupTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_tabControl.SelectedItem is BookGroup selectedGroup)
                CurrentGroup = selectedGroup;
        }

        public void LoadBooks(AppSettings settings)
        {
            var ebooksRoot = settings.EbooksPath;
            if (!Directory.Exists(ebooksRoot)) return;

            var groupsOnDisk = new List<BookGroup>();

            foreach (var groupFolder in Directory.GetDirectories(ebooksRoot))
            {
                var groupName = Path.GetFileName(groupFolder);
                var group = new BookGroup(groupName);

                foreach (var bookFolder in Directory.GetDirectories(groupFolder))
                {
                    // Filter out the "ProcessedBooks" management folder
                    if (Path.GetFileName(bookFolder).Equals("ProcessedBooks", StringComparison.OrdinalIgnoreCase))
                        continue;

                    string title = Path.GetFileName(bookFolder);
                    var book = new Book(title, bookFolder);

                    // Check if processed by looking at the output path defined in BookData
                    bool isProcessed = File.Exists(book.Data.FinalEpubOutputPath);

                    book.Status = isProcessed ? BookStatus.Completed : BookStatus.Idle;
                    book.Progress = isProcessed ? 100 : 0;
                    book.IsProcessed = isProcessed;

                    group.Books.Add(book);
                }

                // Sort: Unprocessed first
                var sortedBooks = group.Books.OrderBy(b => b.IsProcessed).ToList();
                group.Books.Clear();
                foreach (var b in sortedBooks) group.Books.Add(b);

                groupsOnDisk.Add(group);
            }

            SyncTabsWithDisk(groupsOnDisk);

            if (CurrentGroup == null || !Tabs.Contains(CurrentGroup))
                CurrentGroup = Tabs.FirstOrDefault();
        }

        private void SyncTabsWithDisk(List<BookGroup> groupsOnDisk)
        {
            // Remove missing groups
            for (int i = Tabs.Count - 1; i >= 0; i--)
                if (!groupsOnDisk.Any(g => g.Name == Tabs[i].Name)) Tabs.RemoveAt(i);

            foreach (var groupFromDisk in groupsOnDisk)
            {
                var existingGroup = Tabs.FirstOrDefault(g => g.Name == groupFromDisk.Name);
                if (existingGroup == null)
                {
                    Tabs.Add(groupFromDisk);
                }
                else
                {
                    // Sync books within group
                    existingGroup.Books.Clear();
                    foreach (var b in groupFromDisk.Books) existingGroup.Books.Add(b);
                }
            }
        }

        public void AddBook(string title, string sourceEpub, List<string> sourceMp3s)
        {
            if (CurrentGroup == null) throw new InvalidOperationException("No group selected.");

            string sanitizedTitle = SanitizeFolderName(title);
            string bookFolder = Path.Combine(_settings.EbooksPath, CurrentGroup.Name, sanitizedTitle);

            var newBook = new Book(sanitizedTitle, bookFolder);
            newBook.Data.EnsureDirectories();

            // Copy EPUB to internal structure
            if (File.Exists(sourceEpub))
            {
                string target = Path.Combine(newBook.Data.EpubPathDir, Path.GetFileName(sourceEpub));
                File.Copy(sourceEpub, target, true);
            }

            // Copy MP3s to internal structure
            foreach (var mp3 in sourceMp3s)
            {
                if (File.Exists(mp3))
                {
                    string target = Path.Combine(newBook.Data.AudioPathDir, Path.GetFileName(mp3));
                    File.Copy(mp3, target, true);
                }
            }

            CurrentGroup.Books.Add(newBook);
            newBook.RefreshDataState();
        }

        public void RemoveBook(Book book)
        {
            if (book == null || CurrentGroup == null) return;

            if (Directory.Exists(book.Data.RootFolder))
                Directory.Delete(book.Data.RootFolder, true);

            // Clean up global processed file if it exists
            if (File.Exists(book.Data.FinalEpubOutputPath))
                File.Delete(book.Data.FinalEpubOutputPath);

            CurrentGroup.Books.Remove(book);
        }

        public static string SanitizeFolderName(string folderName)
        {
            char[] invalidChars = Path.GetInvalidFileNameChars();
            foreach (var c in invalidChars) folderName = folderName.Replace(c, '_');
            return folderName.Trim();
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string propertyName = null)
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
}
