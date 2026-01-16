using System.Windows;
using System.Windows.Input;
using ErenshorModInstaller.Wpf.Services;

namespace ErenshorModInstaller.Wpf
{
    public partial class SaveProfileDialog : Window
    {
        public string ProfileName { get; private set; } = "";
        public bool WasSaved { get; private set; } = false;

        public SaveProfileDialog()
        {
            InitializeComponent();
            ProfileNameTextBox.Focus();
            
            // Handle Enter key to save
            ProfileNameTextBox.KeyDown += (s, e) =>
            {
                if (e.Key == Key.Enter && !string.IsNullOrWhiteSpace(ProfileNameTextBox.Text))
                {
                    Save_Click(s, e);
                }
            };
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            var name = ProfileNameTextBox.Text?.Trim() ?? "";
            
            // Validate name
            if (string.IsNullOrWhiteSpace(name))
            {
                MessageBox.Show(
                    "Please enter a profile name.", 
                    "Validation Error", 
                    MessageBoxButton.OK, 
                    MessageBoxImage.Warning);
                ProfileNameTextBox.Focus();
                return;
            }

            // Check if profile name already exists
            if (ProfileManager.ProfileNameExists(name))
            {
                var result = MessageBox.Show(
                    $"A profile named '{name}' already exists. Do you want to overwrite it?",
                    "Profile Exists",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result != MessageBoxResult.Yes)
                {
                    ProfileNameTextBox.Focus();
                    ProfileNameTextBox.SelectAll();
                    return;
                }
            }

            ProfileName = name;
            WasSaved = true;
            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            WasSaved = false;
            DialogResult = false;
            Close();
        }
    }
}
