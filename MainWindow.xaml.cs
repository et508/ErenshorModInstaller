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

        public MainWindow()
        {
            InitializeComponent();
            AppVersion = $"v{UpdateChecker.GetCurrentVersion()}";
            DataContext = this;
            
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
        
        private void InitializeGamePaths()
        {
            var settings = AppDataManager.LoadSettings();
            
            if (settings.GameInstallPaths.Count == 0)
            {
                var detectedPath = TryAutoDetectErenshor();
                if (!string.IsNullOrEmpty(detectedPath))
                {
                    AppDataManager.AddGameInstallPath(detectedPath, "Live");
                    Info("Auto-detected Erenshor via Steam.");
                }
                else
                {
                    Warn("Could not auto-detect Erenshor. Use the browse button to set the game folder.");
                }
            }
            
            LoadGamePathsIntoComboBox();
            
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
        
        private string? TryAutoDetectErenshor()
        {
            try
            {
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
        
        private void LoadGamePathsIntoComboBox()
        {
            _isInitializing = true;
            GamePathComboBox.Items.Clear();

            var settings = AppDataManager.LoadSettings();
            
            var livePath = settings.GameInstallPaths.FirstOrDefault(e => e.Label == "Live")?.Path;
            var playtestPath = settings.GameInstallPaths.FirstOrDefault(e => e.Label == "Playtest")?.Path;
            
            var currentPath = AppDataManager.GetGameInstallPath();
            ComboBoxPathItem? itemToSelect = null;
            
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
            
            if (!string.IsNullOrEmpty(currentPath))
            {
                if (string.Equals(currentPath, livePath, StringComparison.OrdinalIgnoreCase))
                    itemToSelect = liveItem;
                else if (string.Equals(currentPath, playtestPath, StringComparison.OrdinalIgnoreCase))
                    itemToSelect = playtestItem;
            }
            
            if (itemToSelect == null)
            {
                if (string.IsNullOrEmpty(livePath) && string.IsNullOrEmpty(playtestPath))
                {
                    liveItem.Label = "Erenshor Not Detected";
                    itemToSelect = liveItem;
                }
                else
                {
                    itemToSelect = !string.IsNullOrEmpty(livePath) ? liveItem : playtestItem;
                }
            }
            
            if (itemToSelect != null)
            {
                GamePathComboBox.SelectedItem = itemToSelect;
            }
            
            _isInitializing = false;
        }
        
        private async void GamePathComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isInitializing) return;
            
            if (GamePathComboBox.SelectedItem is ComboBoxPathItem item)
            {
                if (string.IsNullOrEmpty(item.Path))
                {
                    var installTypeName = item.InstallType == InstallationType.Live ? "Live" : "Playtest";
                    Info($"{installTypeName} path not set. Click the browse button (📁) to select the game folder.");
                    return;
                }
                
                if (Directory.Exists(item.Path))
                {
                    var label = item.InstallType == InstallationType.Live ? "Live" : "Playtest";
                    AppDataManager.SaveGameInstallPath(item.Path, label);
                    
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
                    
                    var label = item.InstallType == InstallationType.Live ? "Live" : "Playtest";
                    AppDataManager.AddGameInstallPath(selectedPath, label);
                    
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
    }
    
    public class ComboBoxPathItem
    {
        public string Label { get; set; } = "";
        public string Path { get; set; } = "";
        public bool IsEnabled { get; set; } = true;
        public InstallationType InstallType { get; set; } = InstallationType.None;
        
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
