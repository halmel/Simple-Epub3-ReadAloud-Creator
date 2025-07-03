using System.Collections.ObjectModel;
using System.Windows;

namespace Readaloud_Epub3_Creator
{
    public partial class MoveBooksWindow : Window
    {
        public string? SelectedGroupName { get; private set; }

        public ObservableCollection<BookGroup> Groups { get; set; }

        public MoveBooksWindow(ObservableCollection<BookGroup> groups)
        {
            InitializeComponent();
            Groups = groups;
            GroupsComboBox.ItemsSource = Groups;
            GroupsComboBox.DisplayMemberPath = "Name";
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            if (!string.IsNullOrWhiteSpace(NewGroupNameTextBox.Text))
            {
                SelectedGroupName = NewGroupNameTextBox.Text.Trim();
                DialogResult = true;
            }
            else if (GroupsComboBox.SelectedItem is BookGroup selectedGroup)
            {
                SelectedGroupName = selectedGroup.Name;
                DialogResult = true;
            }
            else
            {
                MessageBox.Show("Please select an existing group or enter a new group name.", "Move Books", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}
