using Microsoft.Extensions.DependencyInjection;
using System;
using System.IO;
using System.Windows;
using ModernWpf.Controls;
using Microsoft.Win32;
using System.Windows.Controls;
using System.Runtime;

namespace Readaloud_Epub3_Creator
{
    public partial class SettingsWindow : Window
    {
        private readonly JsonSettingsProvider _settingsProvider;
        private readonly AppSettings _settings;

        public SettingsWindow()
        {
            InitializeComponent();

            _settingsProvider = App.Services.GetRequiredService<JsonSettingsProvider>();
            _settings = _settingsProvider.Settings;

            MaxConcurrentNumberBox.Value = _settings.MaxConcurrentTranscriptions;
            PathTextBox.Text = _settings.EbooksPath;
            DeviceComboBox.Text = _settings.Device;
            TranscriberPathTextBox.Text = _settings.TranscriberPath;
            DataContext = _settings;


        }

        private void Browse_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFolderDialog { };
            if (dialog.ShowDialog() == true)
            {
                PathTextBox.Text = dialog.FolderName;
            }
        }

        private void BrowseTranscriber_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Title = "Select Python Executable",
                Filter = "Python Executable|python.exe",
                CheckFileExists = true
            };

            if (dialog.ShowDialog() == true)
            {
                TranscriberPathTextBox.Text = dialog.FileName;
            }
        }
        private void DeleteSettings_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show("Are you sure you want to delete all settings and restore defaults?",
                "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                _settingsProvider.DeleteSettingsFile();
                _settingsProvider.Reload(); // in case you want to keep using it in the same session

                MessageBox.Show("Settings deleted. Please restart the app to load defaults.", "Deleted",
                    MessageBoxButton.OK, MessageBoxImage.Information);

                Close();
            }
        }



        private void Save_Click(object sender, RoutedEventArgs e)
        {
            _settings.EbooksPath = PathTextBox.Text;
            _settings.Device = DeviceComboBox.Text;
            _settings.MaxConcurrentTranscriptions = (int)MaxConcurrentNumberBox.Value;
            _settings.TranscriberPath = TranscriberPathTextBox.Text;
            _settingsProvider.Save();

            MessageBox.Show("Settings saved. Please restart the app for changes to apply.", "Saved",
                MessageBoxButton.OK, MessageBoxImage.Information);

            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }


    }
}
