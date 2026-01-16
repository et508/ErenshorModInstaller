using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using ErenshorModInstaller.Wpf.Services;
using ErenshorModInstaller.Wpf.Services.Models;

namespace ErenshorModInstaller.Wpf
{
    public partial class ProfileManagementDialog : Window
    {
        private List<ProfileDisplayItem> _profiles = new List<ProfileDisplayItem>();
        public bool ProfilesChanged { get; private set; } = false;

        public ProfileManagementDialog()
        {
            InitializeComponent();
            LoadProfiles();
        }

        private void LoadProfiles()
        {
            var profiles = ProfileManager.GetAllProfiles();
            
            if (profiles.Count == 0)
            {
                EmptyState.Visibility = Visibility.Visible;
                ProfilesList.Visibility = Visibility.Collapsed;
                return;
            }

            EmptyState.Visibility = Visibility.Collapsed;
            ProfilesList.Visibility = Visibility.Visible;

            _profiles = profiles.Select(p => new ProfileDisplayItem(p)).ToList();
            ProfilesList.ItemsSource = _profiles;
        }

        private void Rename_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is ProfileDisplayItem item)
            {
                var dialog = new InputDialog("Enter new name:", "Rename Profile", item.Profile.Name);
                if (dialog.ShowDialog() == true)
                {
                    var newName = dialog.ResponseText?.Trim() ?? "";
                    
                    if (string.IsNullOrWhiteSpace(newName))
                    {
                        MessageBox.Show("Profile name cannot be empty.", "Invalid Name", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }

                    if (ProfileManager.ProfileNameExists(newName, item.Profile.Id))
                    {
                        MessageBox.Show($"A profile named '{newName}' already exists.", "Duplicate Name", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return;
                    }

                    if (ProfileManager.RenameProfile(item.Profile.Id, newName))
                    {
                        ProfilesChanged = true;
                        LoadProfiles();
                    }
                }
            }
        }

        private void Delete_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is ProfileDisplayItem item)
            {
                var result = MessageBox.Show(
                    $"Are you sure you want to delete the profile '{item.Profile.Name}'?\n\nThis action cannot be undone.",
                    "Confirm Delete",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (result == MessageBoxResult.Yes)
                {
                    if (ProfileManager.DeleteProfile(item.Profile.Id))
                    {
                        ProfilesChanged = true;
                        
                        // If this was the active profile, clear it
                        if (AppDataManager.GetActiveProfileId() == item.Profile.Id)
                        {
                            AppDataManager.SetActiveProfileId(null);
                        }
                        
                        LoadProfiles();
                    }
                }
            }
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        /// <summary>
        /// Display wrapper for profiles in the list
        /// </summary>
        private class ProfileDisplayItem
        {
            public ModProfile Profile { get; }
            public string Name => Profile.Name;
            public string Description => Profile.Description;
            public Visibility HasDescription => string.IsNullOrWhiteSpace(Profile.Description) ? Visibility.Collapsed : Visibility.Visible;
            public int ModCount => Profile.EnabledModGuids.Count;
            public string LastUsedText
            {
                get
                {
                    var span = DateTime.UtcNow - Profile.LastUsed;
                    if (span.TotalMinutes < 1) return "just now";
                    if (span.TotalHours < 1) return $"{(int)span.TotalMinutes}m ago";
                    if (span.TotalDays < 1) return $"{(int)span.TotalHours}h ago";
                    if (span.TotalDays < 7) return $"{(int)span.TotalDays}d ago";
                    return Profile.LastUsed.ToLocalTime().ToString("MMM d");
                }
            }

            public ProfileDisplayItem(ModProfile profile)
            {
                Profile = profile;
            }
        }
    }
}
