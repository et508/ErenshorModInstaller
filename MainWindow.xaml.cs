using Microsoft.Win32;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
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

        public MainWindow()
        {
            InitializeComponent();
            AppVersion = $"v{UpdateChecker.GetCurrentVersion()}";
            DataContext = this;
            
            // Initialize AppData folder and load settings
            AppDataManager.EnsureAppDataFolder();
            InitializeGamePaths();
            
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
        /// Opens a folder browser to select a new game installation path
        /// </summary>
        private void BrowseForGamePath()
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
                var gameRoot = Path.GetDirectoryName(exePath);

                if (string.IsNullOrEmpty(gameRoot))
                {
                    Error("Invalid game folder selected.");
                    return;
                }

                // Ask user which installation type this is
                var result = MessageBox.Show(
                    "Is this the Live or Playtest version?\n\nClick Yes for Live, No for Playtest.",
                    "Installation Type",
                    MessageBoxButton.YesNoCancel,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Cancel)
                    return;

                var label = result == MessageBoxResult.Yes ? "Live" : "Playtest";
                
                AppDataManager.AddGameInstallPath(gameRoot, label);
                Info($"Added {label} installation: {gameRoot}");
                
                LoadGamePathsIntoComboBox();
            }
        }

        /// <summary>
        /// Handles browse button click from within the ComboBox dropdown
        /// </summary>
        private async void BrowseGamePath_Click(object sender, RoutedEventArgs e)
        {
            // Stop the event from bubbling up to ComboBox
            e.Handled = true;

            if (sender is Button button && button.Tag is ComboBoxPathItem item)
            {
                var dialog = new OpenFileDialog
                {
                    Title = $"Select Erenshor.exe for {item.InstallType}",
                    Filter = "Erenshor Executable|Erenshor.exe",
                    CheckFileExists = true
                };

                if (dialog.ShowDialog() == true)
                {
                    var exePath = dialog.FileName;
                    var gameRoot = Path.GetDirectoryName(exePath);

                    if (string.IsNullOrEmpty(gameRoot))
                    {
                        Error("Invalid game folder selected.");
                        return;
                    }

                    var label = item.InstallType == InstallationType.Live ? "Live" : "Playtest";
                    AppDataManager.AddGameInstallPath(gameRoot, label);
                    AppDataManager.SaveGameInstallPath(gameRoot, label);
                    Info($"Set {label} installation: {gameRoot}");
                    
                    // Reload and the new path will be selected automatically
                    LoadGamePathsIntoComboBox();
                    
                    // Validate and refresh
                    await AutoValidateAsync();
                    RefreshMods();
                }
                
                // Close the dropdown after dialog
                GamePathComboBox.IsDropDownOpen = false;
            }
        }

        /// <summary>
        /// Gets the current game path from the ComboBox
        /// </summary>
        private string GetCurrentGamePath()
        {
            if (GamePathComboBox.SelectedItem is ComboBoxPathItem item && 
                !string.IsNullOrEmpty(item.Path) && 
                Directory.Exists(item.Path))
            {
                return item.Path;
            }
            
            return string.Empty;
        }

        private async Task AutoValidateAsync()
        {
            var root = GetCurrentGamePath();
            if (!string.IsNullOrEmpty(root))
            {
                await GameSetupService.ValidateAndFixAsync(root, this);
            }
        }

        private void EnableAllMods_Click(object sender, RoutedEventArgs e)
        {
            var root = GetCurrentGamePath();
            if (string.IsNullOrWhiteSpace(root))
            {
                Warn("Select game folder first.");
                return;
            }

            try
            {
                int count = 0;
                foreach (var mod in Mods.Where(m => !m.IsEnabled))
                {
                    ModService.Enable(mod);
                    count++;
                }
                
                RefreshMods();
                Info($"Enabled {count} mod(s).");
            }
            catch (Exception ex)
            {
                Error("Failed to enable all mods: " + ex.Message);
            }
        }

        private void DisableAllMods_Click(object sender, RoutedEventArgs e)
        {
            var root = GetCurrentGamePath();
            if (string.IsNullOrWhiteSpace(root))
            {
                Warn("Select game folder first.");
                return;
            }

            try
            {
                int count = 0;
                foreach (var mod in Mods.Where(m => m.IsEnabled))
                {
                    ModService.Disable(mod);
                    count++;
                }
                
                RefreshMods();
                Info($"Disabled {count} mod(s).");
            }
            catch (Exception ex)
            {
                Error("Failed to disable all mods: " + ex.Message);
            }
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
                Warn("BepInEx/plugins not found. Run Erenshor once after installing BepInEx.");
                MessageBox.Show(
                    "The folder BepInEx\\plugins does not exist yet.\n\n" +
                    "Run Erenshor once after installing BepInEx so it can complete setup.",
                    "Plugins folder missing",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = plugins, UseShellExecute = true });
        }

        private void LaunchErenshor_Click(object sender, RoutedEventArgs e)
        {
            var root = GetCurrentGamePath();
            if (string.IsNullOrWhiteSpace(root))
            {
                Warn("Select game folder first.");
                return;
            }

            var exe = System.IO.Path.Combine(root, "Erenshor.exe");
            if (File.Exists(exe))
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = exe,
                    WorkingDirectory = root,
                    UseShellExecute = true
                });
                Info("Launching Erenshor…");
                //Close();
            }
            else
            {
                Warn("Erenshor.exe not found in this folder.");
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = root, UseShellExecute = true });
            }
        }

        private void InstallZip_Click(object sender, RoutedEventArgs e)
        {
            var ofd = new OpenFileDialog
            {
                Title = "Choose a Mod archive",
                Filter = "Archives|*.zip;*.7z;*.rar|Zip|*.zip|7-Zip|*.7z|RAR|*.rar|All files|*.*"
            };
            if (ofd.ShowDialog() == true)
            {
                InstallAny(ofd.FileName);
            }
        }

        private void RefreshMods_Click(object sender, RoutedEventArgs e) => RefreshMods();

        private void Uninstall_Click(object sender, RoutedEventArgs e)
        {
            var root = GetCurrentGamePath();
            if (string.IsNullOrWhiteSpace(root))
            {
                Warn("Select game folder first.");
                return;
            }

            if (ModsList.SelectedItem is ModItem item)
            {
                if (ModService.Uninstall(root, item, this))
                {
                    RefreshMods();
                }
            }
            else
            {
                Info("Select a mod to uninstall.");
            }
        }

        private void ModToggle_Checked(object sender, RoutedEventArgs e)
        {
            if (_isUpdatingList) return;
            var item = (sender as FrameworkElement)?.DataContext as ModItem;
            if (item == null) return;

            try
            {
                ModService.Enable(item);
                Info($"Enabled: {item.DisplayName}");
            }
            catch (Exception ex) { Error("Enable failed: " + ex.Message); }
            finally { RefreshMods(); }
        }

        private void ModToggle_Unchecked(object sender, RoutedEventArgs e)
        {
            if (_isUpdatingList) return;
            var item = (sender as FrameworkElement)?.DataContext as ModItem;
            if (item == null) return;

            try
            {
                ModService.Disable(item);
                Info($"Disabled: {item.DisplayName}");
            }
            catch (Exception ex) { Error("Disable failed: " + ex.Message); }
            finally { RefreshMods(); }
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
}