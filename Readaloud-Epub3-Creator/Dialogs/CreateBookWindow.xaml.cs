using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using EpubSharp;
using System.IO;

namespace Readaloud_Epub3_Creator
{
    /// <summary>
    /// Interaction logic for CreateBookWindow.xaml
    /// </summary>
    public partial class CreateBookWindow : Window
    {
        public Book CreatedBook { get; private set; }
        private List<string> selectedMp3s = new List<string>();

        public CreateBookWindow()
        {
            InitializeComponent();
        }

        private void BrowseEpub_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog dlg = new OpenFileDialog
            {
                Filter = "EPUB files (*.epub)|*.epub"
            };
            if (dlg.ShowDialog() == true)
            {
                string epubPath = dlg.FileName;
                EpubTextBox.Text = epubPath;

                // Extract title from EPUB
                try
                {
                    EpubBook epub = EpubReader.Read(epubPath);
                    string title = epub.Title ?? System.IO.Path.GetFileNameWithoutExtension(dlg.FileName);

                    TitleTextBox.Text = title;
                    TitleTextBox.IsEnabled = true;
                }
                catch
                {
                    MessageBox.Show("Failed to read EPUB metadata.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    TitleTextBox.Text = string.Empty;
                    TitleTextBox.IsEnabled = false;
                }
            }
        }

        private void BrowseMp3_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog dlg = new OpenFileDialog
            {
                Filter = "MP3 files (*.mp3)|*.mp3",
                Multiselect = true
            };
            if (dlg.ShowDialog() == true)
            {
                selectedMp3s = new List<string>(dlg.FileNames);
                Mp3TextBox.Text = $"{selectedMp3s.Count} file(s) selected";
            }
        }

        private void Create_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(TitleTextBox.Text) || string.IsNullOrWhiteSpace(EpubTextBox.Text))
            {
                MessageBox.Show("Please select an EPUB file and ensure the title is set.", "Incomplete", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            CreatedBook = new Book
            {
                Title = TitleTextBox.Text.Trim(),
                EpubFile = EpubTextBox.Text,
                Mp3Files = selectedMp3s
            };

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
