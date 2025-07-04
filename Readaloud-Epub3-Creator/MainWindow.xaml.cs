using EpubSharp;
using FuzzySharp;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;
using Newtonsoft.Json;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using static Readaloud_Epub3_Creator.Book;
namespace Readaloud_Epub3_Creator
{
    public partial class MainWindow : Window
    {
        private static bool consoleAllocated = false;

        [DllImport("kernel32.dll")]
        static extern bool AllocConsole();

        [DllImport("kernel32.dll")]
        static extern IntPtr GetConsoleWindow();

        [DllImport("user32.dll")]
        static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        const int SW_HIDE = 0;
        const int SW_SHOW = 5;
        private bool IsConsoleVisible()
        {
            IntPtr handle = GetConsoleWindow();
            return handle != IntPtr.Zero && IsWindowVisible(handle);
        }

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        static extern bool IsWindowVisible(IntPtr hWnd);

        private readonly JsonSettingsProvider _settingsProvider;
        private readonly AppSettings _settings;

        public AppSettings Settings => _settings;  // expose as a property


        private string EbooksRoot => _settings.EbooksPath;
        private BookgroupList groups = new();


        private readonly Queue<Book> processingQueue = new();
        private bool isProcessing = false;

        public bool ShowDebugOptions => _settings.ShowDebugOptions;




        public MainWindow()
        {
            InitializeComponent();


            // Allocate and immediately hide the console on startup
            if (!consoleAllocated)
            {
                AllocConsole();
                consoleAllocated = true;
                ShowWindow(GetConsoleWindow(), SW_HIDE);
            }




            // Retrieve settings from DI
            _settingsProvider = App.Services.GetRequiredService<JsonSettingsProvider>();
            _settings = _settingsProvider.Settings;
            EnsureDirectoryStructure();

            groups = new BookgroupList(_settings, GroupTabs);
            this.DataContext = groups;


        }

        private void GroupTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (GroupTabs.SelectedItem is BookGroup selectedGroup)
            {
                groups.CurrentGroup = selectedGroup;
            }
        }

        private void Settings_Click(object sender, RoutedEventArgs e)
        {
            var settingsWindow = new SettingsWindow { Owner = this };
            settingsWindow.ShowDialog();
        }

        private void CreateBook_Click(object sender, RoutedEventArgs e)
        {
            var window = new CreateBookWindow { Owner = this };

            if (window.ShowDialog() == true)
            {
                var book = window.CreatedBook;
                groups.AddBook(book,_settings);
                groups.LoadBooks(_settings);
            }
        }

        // Selects all unprocessed and idle books
        private void SelectAllUnprocessed_Click(object sender, RoutedEventArgs e)
        {
            var unprocessedIdleBooks = groups.CurrentGroup.Books
                .Where(book => !book.IsProssed && book.Status == BookStatus.Idle);

            foreach (var book in unprocessedIdleBooks)
            {
                book.IsSelected = true;
            }
        }
        private void ToggleSelectAll_Click(object sender, RoutedEventArgs e)
        {
            var allSelected = groups.CurrentGroup.Books.All(book => book.IsSelected);

            bool newSelectionState = !allSelected;

            foreach (var book in groups.CurrentGroup.Books)
            {
                book.IsSelected = newSelectionState;
            }

            // Update button text
            if (sender is Button btn)
            {
                btn.Content = newSelectionState ? "Unselect All" : "Select All";
            }
        }


        // Enqueues and starts processing all selected books
        private void AlignAllSelected_Click(object sender, RoutedEventArgs e)
        {
            var selectedBooks = groups.CurrentGroup.Books
                .Where(book => book.IsSelected)
                .ToList();

            if (!selectedBooks.Any())
            {
                MessageBox.Show("No books are selected.", "Align Selected Books", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            foreach (var book in selectedBooks)
            {
                if (book.Status == BookStatus.Idle && !book.IsProssed)
                {
                    book.Status = BookStatus.WaitingInQueue;
                    processingQueue.Enqueue(book);
                }
            }

            UpdateQueuePositions();
            ProcessNextInQueue(); // Start processing if not already
        }
        private void MoveSelectedBooks_Click(object sender, RoutedEventArgs e)
        {
            var selectedBooks = groups.CurrentGroup.Books.Where(b => b.IsSelected).ToList();

            if (!selectedBooks.Any())
            {
                MessageBox.Show("No books are selected to move.", "Move Books", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var moveWindow = new MoveBooksWindow(groups.Tabs) { Owner = this };

            if (moveWindow.ShowDialog() == true)
            {
                string targetGroupName = moveWindow.SelectedGroupName!;

                if (string.IsNullOrWhiteSpace(targetGroupName))
                {
                    MessageBox.Show("Invalid target group name.", "Move Books", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var targetGroup = groups.Tabs.FirstOrDefault(g => g.Name == targetGroupName);
                if (targetGroup == null)
                {
                    MessageBox.Show("Target group not found.", "Move Books", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                foreach (var book in selectedBooks)
                {
                    try
                    {
                        groups.MoveBookToGroup(book, targetGroup, _settings);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Failed to move book '{book.Title}': {ex.Message}", "Move Books", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }

                // Refresh UI
                groups.LoadBooks(_settings);
            }
        }

        private void Save_Groups_Json(object sender, RoutedEventArgs e)
        {

            string json = JsonConvert.SerializeObject(groups, Formatting.Indented,
                new JsonSerializerSettings { PreserveReferencesHandling = PreserveReferencesHandling.Objects });

            string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "DesignTimePreviewData.json");
            File.WriteAllText(path, json);

            MessageBox.Show("Design-time preview data saved to:\n" + path);
        }


        private void ImportFromFolder_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFolderDialog();
            if (dialog.ShowDialog() == true)
            {
                string rootPath = dialog.FolderName;
                var epubFiles = Directory.GetFiles(rootPath, "*.epub", SearchOption.AllDirectories).ToList();

                var mp3Groups = Directory.GetDirectories(rootPath, "*", SearchOption.AllDirectories)
                    .Select(dir => new
                    {
                        Folder = dir,
                        Mp3s = Directory.GetFiles(dir, "*.mp3").ToList()
                    })
                    .Where(group => group.Mp3s.Any())
                    .ToList();

                foreach (var epubFilePath in epubFiles)
                {
                    string epubBaseName = Path.GetFileNameWithoutExtension(epubFilePath);

                    // Read EPUB to get the title
                    EpubBook epubBook;
                    string title;

                    try
                    {
                        epubBook = EpubReader.Read(epubFilePath);
                        title = !string.IsNullOrWhiteSpace(epubBook.Title) ? epubBook.Title : epubBaseName;
                    }
                    catch (Exception ex)
                    {
                        // If something fails, fallback to the filename
                        title = epubBaseName;
                    }

                    var bestMatch = mp3Groups
                        .Select(group => new
                        {
                            group.Folder,
                            group.Mp3s,
                            Score = Fuzz.PartialRatio(Path.GetFileName(group.Folder), epubBaseName)
                        })
                        .OrderByDescending(x => x.Score)
                        .FirstOrDefault();

                    if (bestMatch != null && bestMatch.Score > 60)
                    {
                        var book = new Book
                        {
                            Title = title,  // Use the extracted title here
                            EpubFile = epubFilePath,
                            Mp3Files = bestMatch.Mp3s,
                            FolderPath = Path.Combine(EbooksRoot, epubBaseName),
                            IsProssed = false,
                            Progress = 0,
                            Status = BookStatus.Idle
                        };

                        groups.AddBook(book, _settings);
                        groups.CurrentGroup.Books.Add(book);

                        groups.LoadBooks(_settings);
                    }
                }
                groups.LoadBooks(_settings);
            }
        }


        private void CreateGroup_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new CreateGroupWindow { Owner = this };

            if (dialog.ShowDialog() == true)
            {
                string groupName = dialog.GroupName;

                // Check if it already exists
                if (groups.Tabs.Any(g => g.Name.Equals(groupName, StringComparison.OrdinalIgnoreCase)))
                {
                    MessageBox.Show("A group with that name already exists.", "Duplicate Group", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // Create the folder on disk
                string groupFolder = Path.Combine(_settings.EbooksPath, groupName);
                Directory.CreateDirectory(groupFolder);

                // Add new group in memory
                var newGroup = new BookGroup(groupName);
                groups.Tabs.Add(newGroup);
                groups.CurrentGroup = newGroup;
            }
        }


        private void DeleteGroup_Click(object sender, RoutedEventArgs e)
        {
            var currentGroup = groups.CurrentGroup;

            if (currentGroup == null)
            {
                MessageBox.Show("No group selected.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var result = MessageBox.Show($"Are you sure you want to delete the group '{currentGroup.Name}'?\nAll books and files in this group will be permanently deleted.",
                                         "Confirm Delete",
                                         MessageBoxButton.YesNo,
                                         MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    // Delete folder from disk
                    string groupFolderPath = Path.Combine(_settings.EbooksPath, currentGroup.Name);
                    if (Directory.Exists(groupFolderPath))
                    {
                        Directory.Delete(groupFolderPath, recursive: true);
                    }

                    // Remove from UI
                    groups.Tabs.Remove(currentGroup);
                    groups.CurrentGroup = null;

                    // Refresh group list
                    groups.LoadBooks(_settings);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to delete group: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }
        private void DeleteSelectedBooks_Click(object sender, RoutedEventArgs e)
        {
            // Get selected books
            var selectedBooks = groups.CurrentGroup.Books.Where(b => b.IsSelected).ToList();

            if (selectedBooks.Count == 0)
            {
                MessageBox.Show("No books are selected.", "Delete Selected Books", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var result = MessageBox.Show($"Are you sure you want to delete {selectedBooks.Count} selected book(s)?",
                                         "Confirm Deletion",
                                         MessageBoxButton.YesNo,
                                         MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                foreach (var book in selectedBooks)
                {
                    try
                    {
                        // Use your existing remove function for books here
                        groups.RemoveBook(book,_settings);
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"Failed to delete book '{book.Title}': {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }

                // Refresh book list
                groups.LoadBooks(_settings);
            }
        }





        private void ToggleConsole_Click(object sender, RoutedEventArgs e)
        {
            if (!consoleAllocated)
            {
                AllocConsole();
                consoleAllocated = true;
            }
            else
            {
                IntPtr handle = GetConsoleWindow();
                // Toggle visibility
                ShowWindow(handle, IsConsoleVisible() ? SW_HIDE : SW_SHOW);
            }
        }









        private void ViewAlignmentLog_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.DataContext is Book book)
            {
                string logPath = Path.Combine(book.FolderPath, "OriginalEpub", "AlingmentLog.json");

                if (File.Exists(logPath))
                {
                    var viewer = new LogViewerWindow(logPath);
                    viewer.Owner = this;
                    viewer.ShowDialog();
                }
                else
                {
                    MessageBox.Show("Alignment log file not found.");
                }
            }
        }


        private void ReprocessAlignment_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.DataContext is Book book)
            {
                try
                {
                    string originalEpubPath = Path.Combine(book.FolderPath, "OriginalEpub");
                    string alignmentLogPath = Path.Combine(originalEpubPath, "AlignmentLog.json");
                    string wordsJsonPath = Path.Combine(originalEpubPath, "Words.json");

                    if (File.Exists(alignmentLogPath))
                        File.Delete(alignmentLogPath);

                    if (File.Exists(wordsJsonPath))
                        File.Delete(wordsJsonPath);

                    // Reuse existing logic by manually invoking the Align button handler
                    AlignButton_Click(sender, e);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Failed to reprocess alignment:\n{ex.Message}", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }



        private void AlignButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.DataContext is Book book)
            {
                if (book.Status == BookStatus.Idle || book.Status == BookStatus.Completed)
                {
                    book.Status = BookStatus.WaitingInQueue;
                    processingQueue.Enqueue(book);
                    UpdateQueuePositions();
                    ProcessNextInQueue(); // Start processing if not already
                }
            }
        }

        private void UpdateQueuePositions()
        {
            int index = 0;
            foreach (var b in processingQueue)
            {
                b.QueuePosition = index++;
            }
        }

        private async void ProcessNextInQueue()
        {
            if (isProcessing || processingQueue.Count == 0)
                return;

            isProcessing = true;

            var book = processingQueue.Dequeue();
            book.Status = BookStatus.Running;
            book.QueuePosition = -1;
            UpdateQueuePositions();

            var progress = new Progress<int>(p => book.Progress = p);

            await Task.Run(() =>
            {
                GenerateEpubUtil.GenerateEpub(_settings, book.FolderPath, progress);
            });

            book.Status = BookStatus.Completed;
            book.Progress = 100;

            // Update alignment log status
            book.RefreshAlignmentLogStatus();


            isProcessing = false;

            // Process next in queue
            ProcessNextInQueue();
        }



        private void EnsureDirectoryStructure()
        {
            if (!Directory.Exists(EbooksRoot))
                Directory.CreateDirectory(EbooksRoot);
        }

 



    }
   






}





