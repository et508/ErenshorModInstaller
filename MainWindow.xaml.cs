using Microsoft.Win32;
using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ErenshorModInstaller.Wpf.Services;
using ErenshorModInstaller.Wpf.Services.Abstractions;
using ErenshorModInstaller.Wpf.Services.Models;
using ErenshorModInstaller.Wpf.UI;

namespace ErenshorModInstaller.Wpf
{
    public partial class MainWindow : Window, IStatusSink
    {
        public string AppVersion { get; } 
        public ObservableCollection<ModItem> Mods { get; } = new();
        private bool _isUpdatingList;
        private bool _isInitializing = true;
        private bool _isLoadingProfile = false; // Prevents profile from being cleared when loading

        public MainWindow()
        {
            InitializeComponent();
            AppVersion = $"v{UpdateChecker.GetCurrentVersion()}";
            DataContext = this;
            
            // Initialize AppData folder and load settings
            AppDataManager.EnsureAppDataFolder();
            InitializeGamePaths();
            InitializeProfiles();
            
            _isInitializing = false;
            
            _ = Task.Run(async () =>
            {
                var (hasUpdate, latest, _) = await UpdateChecker.CheckAsync();

                if (hasUpdate && latest != null)
                {
                    Dispatcher.Invoke(() =>
                    {
                        var update = Prompts.ShowUpdateAvailable(UpdateChecker.GetCurrentVersion(), latest.Tag);
                        
                        if (update == PromptResult.Primary)
                        {
                            try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = latest.HtmlUrl, UseShellExecute = true }); } catch { }
                        }
                    });
                }
            });
        }

        /// <summary>
        /// Initializes game paths on startup - auto-detects on first run
        /// </summary>
        private void InitializeGamePaths()
        {
            var settings = AppDataManager.LoadSettings();

            // First run - no paths saved yet, try auto-detect
            if (settings.GameInstallPaths.Count == 0)
            {
                var detectedPath = TryAutoDetectErenshor();
                if (!string.IsNullOrEmpty(detectedPath))
                {
                    // Save with "Live" label (default installation)
                    AppDataManager.AddGameInstallPath(detectedPath, "Live");
                    Info("Auto-detected Erenshor via Steam.");
                }
                else
                {
                    Warn("Could not auto-detect Erenshor. Use the browse button to set the game folder.");
                }
            }

            // Load all paths into ComboBox
            LoadGamePathsIntoComboBox();
            
            // Auto-validate and refresh mods for the selected path
            if (!string.IsNullOrEmpty(GetCurrentGamePath()))
            {
                _ = Task.Run(async () =>
                {
                    await Dispatcher.InvokeAsync(async () =>
                    {
                        await AutoValidateAsync();
                        RefreshMods();
                    });
                });
            }
        }

        /// <summary>
        /// Attempts to auto-detect Erenshor installation via Steam
        /// </summary>
        private string? TryAutoDetectErenshor()
        {
            try
            {
                // Use existing SteamLocator service
                var erRoot = SteamLocator.TryFindErenshorRoot();
                if (!string.IsNullOrEmpty(erRoot))
                {
                    return erRoot;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Auto-detect failed: {ex.Message}");
            }
            
            return null;
        }

        /// <summary>
        /// Loads game install paths into the ComboBox with Live/Playtest structure
        /// </summary>
        private void LoadGamePathsIntoComboBox()
        {
            _isInitializing = true;
            GamePathComboBox.Items.Clear();

            var settings = AppDataManager.LoadSettings();
            
            // Get the paths for Live and Playtest
            var livePath = settings.GameInstallPaths.FirstOrDefault(e => e.Label == "Live")?.Path;
            var playtestPath = settings.GameInstallPaths.FirstOrDefault(e => e.Label == "Playtest")?.Path;
            
            // Determine which installation to select by default
            var currentPath = AppDataManager.GetGameInstallPath();
            ComboBoxPathItem? itemToSelect = null;

            // Always add Live and Playtest entries
            var liveItem = new ComboBoxPathItem
            {
                Label = string.IsNullOrEmpty(livePath) ? "Live (Not Set)" : "Live",
                Path = livePath ?? "",
                InstallType = InstallationType.Live
            };
            GamePathComboBox.Items.Add(liveItem);
            
            var playtestItem = new ComboBoxPathItem
            {
                Label = string.IsNullOrEmpty(playtestPath) ? "Playtest (Not Set)" : "Playtest",
                Path = playtestPath ?? "",
                InstallType = InstallationType.Playtest
            };
            GamePathComboBox.Items.Add(playtestItem);

            // Determine which to select
            if (!string.IsNullOrEmpty(currentPath))
            {
                // Select based on current path
                if (string.Equals(currentPath, livePath, StringComparison.OrdinalIgnoreCase))
                    itemToSelect = liveItem;
                else if (string.Equals(currentPath, playtestPath, StringComparison.OrdinalIgnoreCase))
                    itemToSelect = playtestItem;
            }
            
            // If still nothing selected and no paths are set, show "Erenshor Not Detected" in the selected item
            if (itemToSelect == null)
            {
                if (string.IsNullOrEmpty(livePath) && string.IsNullOrEmpty(playtestPath))
                {
                    // Show "Not Detected" as the selected display, but keep Live/Playtest in dropdown
                    liveItem.Label = "Erenshor Not Detected";
                    itemToSelect = liveItem;
                }
                else
                {
                    // Prefer Live if it exists, otherwise Playtest
                    itemToSelect = !string.IsNullOrEmpty(livePath) ? liveItem : playtestItem;
                }
            }

            // Set selection
            if (itemToSelect != null)
            {
                GamePathComboBox.SelectedItem = itemToSelect;
            }
            
            _isInitializing = false;
        }

        /// <summary>
        /// Handles game path ComboBox selection changes
        /// </summary>
        private async void GamePathComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isInitializing) return;
            
            if (GamePathComboBox.SelectedItem is ComboBoxPathItem item)
            {
                // If path is not set for this installation type, just inform the user
                if (string.IsNullOrEmpty(item.Path))
                {
                    var installTypeName = item.InstallType == InstallationType.Live ? "Live" : "Playtest";
                    Info($"{installTypeName} path not set. Click the browse button (📁) to select the game folder.");
                    return;
                }

                // Valid path selected
                if (Directory.Exists(item.Path))
                {
                    var label = item.InstallType == InstallationType.Live ? "Live" : "Playtest";
                    AppDataManager.SaveGameInstallPath(item.Path, label);
                    
                    // Update the label to remove "Not Set" or "Not Detected" text
                    item.Label = label;
                    
                    Info($"Using: {label}");
                    await AutoValidateAsync();
                    RefreshMods();
                }
                else
                {
                    Warn($"Path no longer exists: {item.Path}");
                }
            }
        }

        /// <summary>
        /// Opens browse dialog for selecting game folder
        /// </summary>
        private void BrowseGamePath_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is ComboBoxPathItem item)
            {
                var dialog = new OpenFileDialog
                {
                    Title = "Select Erenshor.exe",
                    Filter = "Erenshor Executable|Erenshor.exe",
                    CheckFileExists = true
                };

                if (dialog.ShowDialog() == true)
                {
                    var exePath = dialog.FileName;
                    var selectedPath = Path.GetDirectoryName(exePath);
                    
                    if (string.IsNullOrEmpty(selectedPath))
                    {
                        Error("Invalid game folder selected.");
                        return;
                    }

                    // Save the path
                    var label = item.InstallType == InstallationType.Live ? "Live" : "Playtest";
                    AppDataManager.AddGameInstallPath(selectedPath, label);
                    
                    // Reload the ComboBox
                    LoadGamePathsIntoComboBox();
                    
                    Info($"Set {label} path: {selectedPath}");
                }
            }
        }

        private string GetCurrentGamePath()
        {
            return AppDataManager.GetGameInstallPath();
        }

        private async Task AutoValidateAsync()
        {
            var root = GetCurrentGamePath();
            if (!string.IsNullOrWhiteSpace(root))
            {
                await GameSetupService.ValidateAndFixAsync(root, this);
            }
        }

        private void InstallZip_Click(object sender, RoutedEventArgs e)
        {
            var ofd = new OpenFileDialog
            {
                Filter = "Archives (*.zip;*.7z;*.rar)|*.zip;*.7z;*.rar|All Files (*.*)|*.*",
                Title = "Select Mod Archive"
            };
            if (ofd.ShowDialog() == true)
                InstallAny(ofd.FileName);
        }

        private void EnableAllMods_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var root = GetCurrentGamePath();
                if (string.IsNullOrWhiteSpace(root)) { Warn("Select game folder first."); return; }

                foreach (var m in Mods.Where(m => !m.IsEnabled))
                {
                    ModService.Enable(m);
                }
                Info("All mods enabled.");
            }
            catch (Exception ex) { Error("Enable All failed: " + ex.Message); }
            finally { RefreshMods(); }
        }

        private void DisableAllMods_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var root = GetCurrentGamePath();
                if (string.IsNullOrWhiteSpace(root)) { Warn("Select game folder first."); return; }

                foreach (var m in Mods.Where(m => m.IsEnabled))
                {
                    ModService.Disable(m);
                }
                Info("All mods disabled.");
            }
            catch (Exception ex) { Error("Disable All failed: " + ex.Message); }
            finally { RefreshMods(); }
        }

        private void OpenPlugins_Click(object sender, RoutedEventArgs e)
        {
            var root = GetCurrentGamePath();
            if (string.IsNullOrWhiteSpace(root))
            {
                Warn("Select game folder first.");
                return;
            }

            var plugins = PathHelper.GetPluginsDir(root);
            if (!Directory.Exists(plugins))
            {
                Warn("BepInEx/plugins not found.");
                return;
            }

            try { System.Diagnostics.Process.Start("explorer.exe", plugins); }
            catch (Exception ex) { Error("Open folder failed: " + ex.Message); }
        }

        private void RefreshMods_Click(object sender, RoutedEventArgs e)
        {
            RefreshMods();
            Info("Mod list refreshed.");
        }

        private void LaunchErenshor_Click(object sender, RoutedEventArgs e)
        {
            var root = GetCurrentGamePath();
            if (string.IsNullOrWhiteSpace(root))
            {
                Warn("Select game folder first.");
                return;
            }

            var exe = Path.Combine(root, "Erenshor.exe");
            if (!File.Exists(exe))
            {
                Warn("Erenshor.exe not found.");
                return;
            }

            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = exe,
                    WorkingDirectory = root,
                    UseShellExecute = true
                });
                Info("Launched Erenshor.");
            }
            catch (Exception ex)
            {
                Error("Launch failed: " + ex.Message);
            }
        }

        /// <summary>
        /// Handles toggle button click - enables or disables the mod based on current state
        /// </summary>
        private void ModToggle_Click(object sender, RoutedEventArgs e)
        {
            if (_isUpdatingList) return;
            
            var button = sender as Button;
            var item = button?.Tag as ModItem;
            if (item == null) return;

            try
            {
                if (item.IsEnabled)
                {
                    ModService.Disable(item);
                    Info($"Disabled: {item.DisplayName}");
                }
                else
                {
                    ModService.Enable(item);
                    Info($"Enabled: {item.DisplayName}");
                }
            }
            catch (Exception ex)
            {
                Error($"Toggle failed: {ex.Message}");
            }
            finally
            {
                RefreshMods();
            }
        }

        /// <summary>
        /// Handles uninstall button click for individual mods
        /// </summary>
        private void ModUninstall_Click(object sender, RoutedEventArgs e)
        {
            if (_isUpdatingList) return;

            var button = sender as Button;
            var item = button?.Tag as ModItem;
            if (item == null) return;

            var res = MessageBox.Show(
                $"Are you sure you want to uninstall \"{item.DisplayName}\"?",
                "Uninstall Mod",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (res != MessageBoxResult.Yes) return;

            try
            {
                var root = GetCurrentGamePath();
                if (string.IsNullOrWhiteSpace(root))
                {
                    Warn("Select game folder first.");
                    return;
                }

                if (ModService.Uninstall(root, item, this))
                {
                    Info($"Uninstalled: {item.DisplayName}");
                    RefreshMods();
                }
            }
            catch (Exception ex)
            {
                Error("Uninstall failed: " + ex.Message);
            }
        }

        private void Window_DragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                var files = (string[])e.Data.GetData(DataFormats.FileDrop);
                bool ok = files.Any(path =>
                    Directory.Exists(path) ||
                    path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ||
                    path.EndsWith(".7z", StringComparison.OrdinalIgnoreCase) ||
                    path.EndsWith(".rar", StringComparison.OrdinalIgnoreCase) ||
                    path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase));
                e.Effects = ok ? DragDropEffects.Copy : DragDropEffects.None;
            }
            else e.Effects = DragDropEffects.None;

            e.Handled = true;
        }

        private async void Window_Drop(object sender, DragEventArgs e)
        {
            try
            {
                if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
                var paths = (string[])e.Data.GetData(DataFormats.FileDrop);
                
                await AutoValidateAsync();

                foreach (var p in paths) InstallAny(p);
            }
            catch (Exception ex) { Error("Drop install failed: " + ex.Message); }
        }

        private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject
        {
            while (current != null)
            {
                if (current is T match) return match;
                current = VisualTreeHelper.GetParent(current);
            }
            return null;
        }

        private void ModsList_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            try
            {
                var element = e.OriginalSource as DependencyObject;
                var lbi = FindAncestor<ListBoxItem>(element);
                if (lbi == null) return;

                lbi.IsSelected = true;

                var item = lbi.DataContext as ModItem;
                if (item == null) return;

                var root = GetCurrentGamePath();
                var alts = ModService.GetAlternateVersions(root, item);
                if (alts == null || alts.Count == 0)
                {
                    lbi.ContextMenu = null;
                    return;
                }

                var menu = new ContextMenu();
                foreach (var opt in alts)
                {
                    var mi = new MenuItem { Header = $"Switch to {opt.Label}", Tag = opt };
                    mi.Click += (_, __) =>
                    {
                        if (ModService.SwitchToVersion(root, item, opt, this))
                            RefreshMods();
                    };
                    menu.Items.Add(mi);
                }

                lbi.ContextMenu = menu;
                menu.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
                menu.IsOpen = true;

                e.Handled = true;
            }
            catch { }
        }

        private async void InstallAny(string path)
        {
            var root = GetCurrentGamePath();
            if (string.IsNullOrWhiteSpace(root))
            {
                Warn("Select game folder first.");
                return;
            }
            
            await GameSetupService.ValidateAndFixAsync(root, this);
            
            var plugins = PathHelper.GetPluginsDir(root);
            if (!Directory.Exists(plugins))
            {
                Warn("Install blocked: run Erenshor once to create BepInEx\\plugins.");
                return;
            }
            var cfgPath = Path.Combine(PathHelper.GetBepInExDir(root), "config", "BepInEx.cfg");
            if (!File.Exists(cfgPath))
            {
                Warn("Install blocked: run Erenshor once to generate BepInEx.cfg.");
                return;
            }

            if (ModService.InstallAny(root, path, this))
            {
                RefreshMods();
            }
        }

        public void Info(string message)  => SetStatus(message);
        public void Warn(string message)  => SetStatus(message);
        public void Error(string message) => SetStatus(message);
        public void Clear()               => SetStatus(string.Empty);

        private void SetStatus(string msg)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(() => StatusBlock.Text = msg, DispatcherPriority.Background);
            }
            else
            {
                StatusBlock.Text = msg;
            }
        }

        private void RefreshMods()
        {
            _isUpdatingList = true;
            Mods.Clear();

            var root = GetCurrentGamePath();
            if (string.IsNullOrWhiteSpace(root)) { _isUpdatingList = false; return; }

            var plugins = PathHelper.GetPluginsDir(root);
            if (!Directory.Exists(plugins))
            {
                Warn("BepInEx/plugins not found. Run Erenshor once after installing BepInEx.");
                _isUpdatingList = false;
                return;
            }

            ModIndex.EnsureMinimalIndex(root);

            foreach (var m in ModService.ListInstalledMods(root))
                Mods.Add(m);

            _isUpdatingList = false;
        }
        
        private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                try { DragMove(); } catch { }
            }
        }

        private void Minimize_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        #region Profile Management

        /// <summary>
        /// Initializes the profile system on startup
        /// </summary>
        private void InitializeProfiles()
        {
            LoadProfilesIntoComboBox();
            
            // Load active profile if one was set
            var activeProfileId = AppDataManager.GetActiveProfileId();
            if (!string.IsNullOrEmpty(activeProfileId))
            {
                var profile = ProfileManager.GetProfile(activeProfileId);
                if (profile != null)
                {
                    SelectProfileInComboBox(profile.Id);
                }
            }
        }

        /// <summary>
        /// Loads all profiles into the profile ComboBox
        /// </summary>
        private void LoadProfilesIntoComboBox()
        {
            ProfileComboBox.Items.Clear();
            
            // Add "Default Profile" option
            var defaultProfileItem = new ProfileComboBoxItem
            {
                Id = null,
                Name = "Default Profile",
                IsNoProfile = true
            };
            ProfileComboBox.Items.Add(defaultProfileItem);
            
            // Add "Save Current Profile..." option
            var saveNewItem = new ProfileComboBoxItem
            {
                Id = null,
                Name = "💾 Save Current Profile...",
                IsNoProfile = false,
                IsSaveOption = true
            };
            ProfileComboBox.Items.Add(saveNewItem);

            // Add all saved profiles
            var profiles = ProfileManager.GetAllProfiles();
            foreach (var profile in profiles)
            {
                ProfileComboBox.Items.Add(new ProfileComboBoxItem
                {
                    Id = profile.Id,
                    Name = profile.Name,
                    ModCount = profile.EnabledModGuids.Count
                });
            }

            // Select "Default Profile" by default if nothing is active
            if (string.IsNullOrEmpty(AppDataManager.GetActiveProfileId()))
            {
                ProfileComboBox.SelectedIndex = 0;
            }
        }

        /// <summary>
        /// Selects a profile in the ComboBox by ID
        /// </summary>
        private void SelectProfileInComboBox(string? profileId)
        {
            for (int i = 0; i < ProfileComboBox.Items.Count; i++)
            {
                if (ProfileComboBox.Items[i] is ProfileComboBoxItem item)
                {
                    if ((profileId == null && item.IsNoProfile) || item.Id == profileId)
                    {
                        ProfileComboBox.SelectedIndex = i;
                        return;
                    }
                }
            }
        }

        /// <summary>
        /// Handles profile selection change
        /// </summary>
        private void ProfileComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isInitializing || _isLoadingProfile) return;

            if (ProfileComboBox.SelectedItem is ProfileComboBoxItem item)
            {
                // Handle "Save Current Profile..." option
                if (item.IsSaveOption)
                {
                    // Trigger save dialog
                    SaveProfile_Click(sender, e);
                    // Revert selection to active profile or default
                    var activeId = AppDataManager.GetActiveProfileId();
                    SelectProfileInComboBox(activeId);
                    return;
                }
                
                if (item.IsNoProfile)
                {
                    // Clear active profile
                    AppDataManager.SetActiveProfileId(null);
                    Info("Switched to: Default Profile");
                }
                else if (!string.IsNullOrEmpty(item.Id))
                {
                    // Load the selected profile
                    var profile = ProfileManager.GetProfile(item.Id);
                    if (profile != null)
                    {
                        try
                        {
                            _isLoadingProfile = true;
                            
                            ProfileManager.ApplyProfile(profile, Mods);
                            AppDataManager.SetActiveProfileId(profile.Id);
                            
                            RefreshMods();
                            Info($"Loaded profile: {profile.Name}");
                        }
                        catch (Exception ex)
                        {
                            Error($"Failed to load profile: {ex.Message}");
                        }
                        finally
                        {
                            _isLoadingProfile = false;
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Handles Save Profile button click
        /// </summary>
        private void SaveProfile_Click(object sender, RoutedEventArgs e)
        {
            var root = GetCurrentGamePath();
            if (string.IsNullOrWhiteSpace(root))
            {
                Warn("Select game folder first.");
                return;
            }

            var dialog = new SaveProfileDialog { Owner = this };
            if (dialog.ShowDialog() == true && dialog.WasSaved)
            {
                try
                {
                    var profile = ProfileManager.CreateProfileFromMods(
                        dialog.ProfileName,
                        "", // No description
                        Mods);

                    AppDataManager.SetActiveProfileId(profile.Id);
                    LoadProfilesIntoComboBox();
                    SelectProfileInComboBox(profile.Id);

                    Info($"Saved profile: {profile.Name}");
                }
                catch (Exception ex)
                {
                    Error($"Failed to save profile: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Handles Rename Profile button click
        /// </summary>
        private void RenameProfile_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is ProfileComboBoxItem item && !item.IsNoProfile && !string.IsNullOrEmpty(item.Id))
            {
                var profile = ProfileManager.GetProfile(item.Id);
                if (profile == null) return;

                var dialog = new InputDialog("Enter new name:", "Rename Profile", profile.Name) { Owner = this };
                if (dialog.ShowDialog() == true)
                {
                    var newName = dialog.ResponseText?.Trim() ?? "";

                    if (string.IsNullOrWhiteSpace(newName))
                    {
                        Warn("Profile name cannot be empty.");
                        return;
                    }

                    if (ProfileManager.ProfileNameExists(newName, profile.Id))
                    {
                        Warn($"A profile named '{newName}' already exists.");
                        return;
                    }

                    if (ProfileManager.RenameProfile(profile.Id, newName))
                    {
                        LoadProfilesIntoComboBox();
                        SelectProfileInComboBox(profile.Id);
                        Info($"Renamed profile to: {newName}");
                    }
                }
            }
        }

        /// <summary>
        /// Handles Delete Profile button click
        /// </summary>
        private void DeleteProfile_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is ProfileComboBoxItem item && !item.IsNoProfile && !string.IsNullOrEmpty(item.Id))
            {
                var profile = ProfileManager.GetProfile(item.Id);
                if (profile == null) return;

                var result = MessageBox.Show(
                    $"Are you sure you want to delete the profile '{profile.Name}'?\n\nThis action cannot be undone.",
                    "Confirm Delete",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (result == MessageBoxResult.Yes)
                {
                    if (ProfileManager.DeleteProfile(profile.Id))
                    {
                        // If this was the active profile, clear it
                        if (AppDataManager.GetActiveProfileId() == profile.Id)
                        {
                            AppDataManager.SetActiveProfileId(null);
                        }

                        LoadProfilesIntoComboBox();
                        SelectProfileInComboBox(null);
                        Info($"Deleted profile: {profile.Name}");
                    }
                }
            }
        }

        #endregion
    }

    /// <summary>
    /// ComboBox item that displays label but stores the full path
    /// </summary>
    public class ComboBoxPathItem
    {
        public string Label { get; set; } = "";
        public string Path { get; set; } = "";
        public bool IsEnabled { get; set; } = true;
        public InstallationType InstallType { get; set; } = InstallationType.None;
        
        /// <summary>
        /// Returns Visible for Live/Playtest entries, Collapsed for others
        /// </summary>
        public Visibility ShowBrowseButton => 
            (InstallType == InstallationType.Live || InstallType == InstallationType.Playtest) 
                ? Visibility.Visible 
                : Visibility.Collapsed;

        public override string ToString() => Label;
    }

    public enum InstallationType
    {
        None,
        Live,
        Playtest
    }

    /// <summary>
    /// ComboBox item for profile selection
    /// </summary>
    public class ProfileComboBoxItem
    {
        public string? Id { get; set; }
        public string Name { get; set; } = "";
        public int ModCount { get; set; }
        public bool IsNoProfile { get; set; } = false;
        public bool IsSaveOption { get; set; } = false;
        
        /// <summary>
        /// Show rename/delete buttons only for actual profiles, not "Default Profile" or "Save" option
        /// </summary>
        public Visibility ShowButtons => (IsNoProfile || IsSaveOption) ? Visibility.Collapsed : Visibility.Visible;

        public override string ToString() => Name;
    }

    /// <summary>
    /// Converter for button tooltip text based on mod enabled state
    /// </summary>
    public class BoolToToggleTextConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool isEnabled)
            {
                return isEnabled ? "Click to disable mod" : "Click to enable mod";
            }
            return "Toggle mod";
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}
