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

        public MainWindow()
        {
            InitializeComponent();
            AppVersion = $"v{UpdateChecker.GetCurrentVersion()}";
            DataContext = this;
            TryAutoDetect();
            
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

        // ---------- App bootstrap / auto-validate ----------

        private async void TryAutoDetect()
        {
            try
            {
                var erRoot = SteamLocator.TryFindErenshorRoot();
                if (!string.IsNullOrEmpty(erRoot))
                {
                    GamePathBox.Text = erRoot;
                    Info("Detected Erenshor via Steam.");

                    await AutoValidateAsync();
                    RefreshMods();
                }
                else
                {
                    Warn("Could not auto-detect Erenshor. Browse to your game folder.");
                }
            }
            catch (Exception ex) { Error("Auto-detect failed: " + ex.Message); }
        }

        private async Task AutoValidateAsync()
        {
            var root = GamePathBox.Text;
            await GameSetupService.ValidateAndFixAsync(root, this);
        }

        // ---------- UI handlers ----------

        private async void Browse_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Title = "Select Erenshor.exe",
                Filter = "Erenshor executable|Erenshor.exe",
                CheckFileExists = true
            };
            if (dlg.ShowDialog() == true)
            {
                var folder = Path.GetDirectoryName(dlg.FileName)!;
                GamePathBox.Text = folder;
                Info("Game folder set.");
                await AutoValidateAsync();
                RefreshMods();
            }
        }

        private void OpenPlugins_Click(object sender, RoutedEventArgs e)
        {
            var root = GamePathBox.Text;
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
            {
                Warn("Set a valid game folder first.");
                return;
            }

            var plugins = Installer.GetPluginsDir(root);
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
            var root = GamePathBox.Text;
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
            {
                Warn("Set a valid game folder first.");
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
                Close();
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
            if (string.IsNullOrWhiteSpace(GamePathBox.Text))
            {
                Warn("Set game folder first.");
                return;
            }

            if (ModsList.SelectedItem is ModItem item)
            {
                if (ModService.Uninstall(GamePathBox.Text, item, this))
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

                // Ensure BepInEx ready before processing any dropped items
                await AutoValidateAsync();

                foreach (var p in paths) InstallAny(p);
            }
            catch (Exception ex) { Error("Drop install failed: " + ex.Message); }
        }

        // ---------- Right-click switch-version menu ----------

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

                var root = GamePathBox.Text;
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

        // ---------- Install orchestration ----------

        private async void InstallAny(string path)
        {
            var root = GamePathBox.Text;
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
            {
                Warn("Set a valid game folder first.");
                return;
            }

            // Always ensure BepInEx is ready (auto-prompts + first-run if needed)
            await GameSetupService.ValidateAndFixAsync(root, this);

            // If plugins still missing or cfg missing, bail (user may have said "No")
            var plugins = Installer.GetPluginsDir(root);
            if (!Directory.Exists(plugins))
            {
                Warn("Install blocked: run Erenshor once to create BepInEx\\plugins.");
                return;
            }
            var cfgPath = Path.Combine(Installer.GetBepInExDir(root), "config", "BepInEx.cfg");
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

        // ---------- IStatusSink implementation (thread-safe) ----------

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

        // ---------- Helpers ----------

        private void RefreshMods()
        {
            _isUpdatingList = true;
            Mods.Clear();

            var root = GamePathBox.Text;
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) { _isUpdatingList = false; return; }

            var plugins = Installer.GetPluginsDir(root);
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
    }
}
