using Microsoft.Extensions.DependencyInjection;
using Microsoft.Win32;
using ModernWpf.Controls;
using Epub3MediaOverlays.Core.AlingnerUtil;
using System;
using System.IO;
using System.Reflection;
using System.Runtime;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;


namespace Epub3MediaOverlays.Wpf
{
    public partial class SettingsWindow : Window
    {
        private List<SettingViewModel> _allSettings = new();

        public SettingsWindow()
        {
            InitializeComponent();
            var settings = App.Services.GetRequiredService<JsonSettingsProvider>().Settings;
            GenerateSettingsUI(settings);
        }

        private void GenerateSettingsUI(AppSettings settings)
        {
            var props = settings.GetType().GetProperties();
            foreach (var p in props)
            {
                var attr = p.GetCustomAttribute<SettingDisplayAttribute>();
                if (attr == null) continue;

                _allSettings.Add(new SettingViewModel
                {
                    Header = attr.Header,
                    Description = attr.Description,
                    Group = attr.Group,
                    EditorControl = CreateEditor(p, settings, attr)
                });
            }

            // Add Aligner Configuration properties
            if (settings.AlingnerConfig != null)
            {
                var alingnerProps = settings.AlingnerConfig.GetType().GetProperties();
                foreach (var p in alingnerProps)
                {
                    _allSettings.Add(new SettingViewModel
                    {
                        Header = FormatPropertyName(p.Name),
                        Description = $"Aligner parameter: {p.Name}",
                        Group = "Aligner",
                        EditorControl = CreateEditor(p, settings.AlingnerConfig, null)
                    });
                }
            }

            // Create Tabs from groups
            var groups = _allSettings.Select(x => x.Group).Distinct().OrderBy(x => x == "Advanced");
            foreach (var g in groups)
                SettingsNav.MenuItems.Add(new ModernWpf.Controls.NavigationViewItem { Content = g, Tag = g });

            SettingsNav.SelectedItem = SettingsNav.MenuItems[0];
        }

        private string FormatPropertyName(string propertyName)
        {
            // Convert CamelCase to Title Case
            var result = new System.Text.StringBuilder();
            foreach (char c in propertyName)
            {
                if (char.IsUpper(c) && result.Length > 0)
                    result.Append(' ');
                result.Append(c);
            }
            return result.ToString();
        }

        private UIElement CreateEditor(PropertyInfo p, object source, SettingDisplayAttribute attr)
        {
            // 1. Folder Picker Logic (TextBox + Button)
            if (attr?.IsFolderPicker == true)
            {
                var grid = new Grid();
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var box = new TextBox { IsReadOnly = true };
                box.SetBinding(TextBox.TextProperty, new Binding(p.Name) { Source = source });

                var btn = new Button { Content = "Browse", Margin = new Thickness(10, 0, 0, 0) };
                btn.Click += (s, e) => {
                    var dialog = new Microsoft.Win32.OpenFolderDialog();
                    if (dialog.ShowDialog() == true) p.SetValue(source, dialog.FolderName);
                };

                grid.Children.Add(box);
                grid.Children.Add(btn);
                Grid.SetColumn(btn, 1);
                return grid;
            }

            // 2. Numeric Input for int
            if (p.PropertyType == typeof(int))
            {
                var num = new NumberBox { SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline, Width = 150, HorizontalAlignment = HorizontalAlignment.Left };
                num.SetBinding(NumberBox.ValueProperty, new Binding(p.Name) { Source = source, Mode = BindingMode.TwoWay });
                return num;
            }

            // 3. Numeric Input for double
            if (p.PropertyType == typeof(double))
            {
                var num = new NumberBox { SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Inline, Width = 150, HorizontalAlignment = HorizontalAlignment.Left };
                num.SetBinding(NumberBox.ValueProperty, new Binding(p.Name) { Source = source, Mode = BindingMode.TwoWay });
                return num;
            }

            // 4. Default Text
            var tbox = new TextBox { Width = 300, HorizontalAlignment = HorizontalAlignment.Left };
            tbox.SetBinding(TextBox.TextProperty, new Binding(p.Name) { Source = source, UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged });
            return tbox;
        }

        private void SettingsNav_SelectionChanged(ModernWpf.Controls.NavigationView sender, ModernWpf.Controls.NavigationViewSelectionChangedEventArgs args)
        {
            var selectedGroup = (args.SelectedItem as ModernWpf.Controls.NavigationViewItem)?.Tag?.ToString();
            SettingsItemsControl.ItemsSource = _allSettings.Where(x => x.Group == selectedGroup);
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            App.Services.GetRequiredService<JsonSettingsProvider>().Save();
            // Insert your RestartApplication() logic here
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) => Close();
    }

    // Helper class for the UI
    public class SettingViewModel
    {
        public string Header { get; set; }
        public string Description { get; set; }
        public string Group { get; set; }
        public bool IsAdvanced { get; set; }
        public UIElement EditorControl { get; set; }
    } 
}




