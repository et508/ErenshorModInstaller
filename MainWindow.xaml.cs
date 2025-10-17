using Microsoft.Win32;
using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using ErenshorModInstaller.Wpf.Services;

namespace ErenshorModInstaller.Wpf
{
    public partial class MainWindow : Window
    {
        public sealed class ModItem
        {
            public string Guid { get; set; } = "";
            public string DisplayName { get; set; } = ""; // BepInPlugin Name
            public string Version { get; set; } = "";     // BepInPlugin Version (or "0.0.0")
            public bool IsFolder { get; set; }
            public string? FolderName { get; set; }       // if folder mod
            public string? DllFileName { get; set; }      // if top-level dll mod (baseName.dll)
            public string PluginDllFullPath { get; set; } = ""; // absolute path to the plugin dll (can be .dll or .dll.disabled)
            public bool IsEnabled { get; set; }

            public string StatusSuffix => IsEnabled ? "" : " (disabled)";
        }

        public ObservableCollection<ModItem> Mods { get; } = new();
        private bool _isUpdatingList;

        public MainWindow()
        {
            InitializeComponent();
            DataContext = this;
            TryAutoDetect();
        }

        private void TryAutoDetect()
        {
            try
            {
                var erRoot = SteamLocator.TryFindErenshorRoot();
                if (!string.IsNullOrEmpty(erRoot))
                {
                    GamePathBox.Text = erRoot;
                    RefreshMods();
                    Status("Detected Erenshor via Steam.");
                    RunValidation(showPrompts: false, forceConfigPrompt: true);
                }
                else
                {
                    Status("Could not auto-detect Erenshor. Browse to your game folder.");
                }
            }
            catch (Exception ex)
            {
                Status("Auto-detect failed: " + ex.Message);
            }
        }

        private void Browse_Click(object sender, RoutedEventArgs e)
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
                RefreshMods();
                RunValidation(showPrompts: false, forceConfigPrompt: true);
            }
        }

        private void OpenPlugins_Click(object sender, RoutedEventArgs e)
        {
            var root = GamePathBox.Text;
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
            {
                Status("Set a valid game folder first.");
                return;
            }
            var plugins = Installer.GetPluginsDir(root);
            if (!Directory.Exists(plugins))
            {
                Status("BepInEx/plugins not found. Run Erenshor once after installing BepInEx.");
                MessageBox.Show(
                    "The folder BepInEx\\plugins does not exist yet.\n\n" +
                    "Run Erenshor once after installing BepInEx so it can complete setup.",
                    "Plugins folder missing",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            Process.Start(new ProcessStartInfo { FileName = plugins, UseShellExecute = true });
        }

        private void LaunchErenshor_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var root = GamePathBox.Text;
                if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
                {
                    Status("Set a valid game folder first.");
                    return;
                }

                var exe = Path.Combine(root, "Erenshor.exe");
                if (File.Exists(exe))
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = exe,
                        WorkingDirectory = root,
                        UseShellExecute = true
                    });
                    Status("Launching Erenshor…");
                    Close();
                }
                else
                {
                    Status("Erenshor.exe not found in this folder.");
                    Process.Start(new ProcessStartInfo { FileName = root, UseShellExecute = true });
                }
            }
            catch (Exception ex)
            {
                Status("Failed to launch: " + ex.Message);
            }
        }

        private void Validate_Click(object sender, RoutedEventArgs e) => RunValidation(showPrompts: true, forceConfigPrompt: false);

        private void RunValidation(bool showPrompts, bool forceConfigPrompt)
        {
            try
            {
                var root = GamePathBox.Text;
                if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
                {
                    Status("Set a valid game folder first.");
                    return;
                }

                string? ver;
                try
                {
                    ver = Installer.ValidateBepInExOrThrow(root);
                }
                catch (Exception ex)
                {
                    Status(ex.Message);
                    if (showPrompts)
                    {
                        MessageBox.Show(
                            ex.Message +
                            "\n\nDownload BepInEx 5.x (x64) and extract it into the Erenshor folder.",
                            "BepInEx not detected",
                            MessageBoxButton.OK,
                            MessageBoxImage.Warning);
                    }
                    return;
                }

                var plugins = Installer.GetPluginsDir(root);
                if (!Directory.Exists(plugins))
                {
                    var msg = "BepInEx/plugins folder not found. Run Erenshor once to complete BepInEx setup.";
                    Status(msg);
                    if (showPrompts)
                    {
                        MessageBox.Show(
                            "The folder BepInEx\\plugins does not exist yet.\n\n" +
                            "Run Erenshor once after installing BepInEx so it can create the full folder structure.",
                            "Plugins folder missing",
                            MessageBoxButton.OK,
                            MessageBoxImage.Information);
                    }
                }
                else
                {
                    Status($"BepInEx OK ({ver ?? "version unknown"}).");
                }

                var cfgStatus = Installer.GetBepInExConfigStatus(root, out var cfgPath);
                switch (cfgStatus)
                {
                    case Installer.BepInExConfigStatus.Ok:
                        break;

                    case Installer.BepInExConfigStatus.MissingConfig:
                        Status("BepInEx.cfg not found. Run Erenshor once to let BepInEx generate its configs.");
                        if (showPrompts)
                        {
                            MessageBox.Show(
                                "BepInEx.cfg was not found.\n\n" +
                                "You must run Erenshor once after installing BepInEx so it can create the config files and folder structure.",
                                "BepInEx config missing",
                                MessageBoxButton.OK,
                                MessageBoxImage.Warning);
                        }
                        break;

                    case Installer.BepInExConfigStatus.MissingKey:
                    case Installer.BepInExConfigStatus.WrongValue:
                        Status("Warning: HideManagerGameObject should be TRUE for Erenshor mods to work correctly.");
                        if (showPrompts || forceConfigPrompt)
                        {
                            var fix = MessageBox.Show(
                                $"BepInEx.cfg found at:\n{cfgPath}\n\n" +
                                "Setting 'HideManagerGameObject' should be TRUE for Erenshor mods to work correctly.\n" +
                                "Would you like me to set it to true?",
                                "BepInEx config",
                                MessageBoxButton.YesNo,
                                MessageBoxImage.Question);

                            if (fix == MessageBoxResult.Yes)
                            {
                                try
                                {
                                    Installer.EnsureHideManagerGameObjectTrue(root);
                                    Status("Updated BepInEx.cfg: HideManagerGameObject = true.");
                                }
                                catch (Exception ex2)
                                {
                                    Status("Failed to update config: " + ex2.Message);
                                }
                            }
                        }
                        break;
                }
            }
            catch (Exception ex)
            {
                Status("Validation failed: " + ex.Message);
            }
        }

        private void InstallZip_Click(object sender, RoutedEventArgs e)
        {
            var ofd = new OpenFileDialog
            {
                Title = "Choose a Mod archive",
                Filter = "Archives|*.zip;*.7z;*.rar|Zip|*.zip|7-Zip|*.7z|RAR|*.rar|All files|*.*"
            };
            if (ofd.ShowDialog() == true) InstallAny(ofd.FileName);
        }

        private void RefreshMods_Click(object sender, RoutedEventArgs e) => RefreshMods();

        private void Uninstall_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(GamePathBox.Text))
            {
                Status("Set game folder first.");
                return;
            }

            if (ModsList.SelectedItem is ModItem item)
            {
                try
                {
                    if (item.IsFolder)
                    {
                        var plugins = Installer.GetPluginsDir(GamePathBox.Text);
                        var target = Path.Combine(plugins, item.FolderName!);
                        if (!Directory.Exists(target)) { Status("Folder not found: " + item.FolderName); return; }

                        var confirm = MessageBox.Show(
                            $"Remove mod folder '{item.FolderName}'?",
                            "Confirm Uninstall",
                            MessageBoxButton.YesNo, MessageBoxImage.Question);

                        if (confirm == MessageBoxResult.Yes)
                        {
                            Installer.TryDeleteDirectory(target);
                            Status($"Removed: ./BepInEx/plugins/{item.FolderName}");
                            RefreshMods();
                        }
                    }
                    else
                    {
                        var path = item.PluginDllFullPath;
                        var baseDll = path.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase)
                            ? path[..^(".disabled".Length)]
                            : path;

                        var existing = File.Exists(baseDll) ? baseDll
                                     : (File.Exists(baseDll + ".disabled") ? baseDll + ".disabled" : null);

                        if (existing == null) { Status("File not found."); return; }

                        var confirm = MessageBox.Show(
                            $"Remove DLL '{Path.GetFileName(existing)}' from plugins?",
                            "Confirm Uninstall",
                            MessageBoxButton.YesNo, MessageBoxImage.Question);

                        if (confirm == MessageBoxResult.Yes)
                        {
                            File.Delete(existing);
                            Status($"Removed: ./BepInEx/plugins/{Path.GetFileName(existing)}");
                            RefreshMods();
                        }
                    }
                }
                catch (Exception ex)
                {
                    Status("Uninstall failed: " + ex.Message);
                }
            }
            else
            {
                Status("Select a mod to uninstall.");
            }
        }

        private void ModToggle_Checked(object sender, RoutedEventArgs e)
        {
            if (_isUpdatingList) return;
            var item = (sender as FrameworkElement)?.DataContext as ModItem;
            if (item == null) return;

            try
            {
                var path = item.PluginDllFullPath;
                if (path.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase))
                {
                    var enabledPath = path[..^(".disabled".Length)];
                    if (File.Exists(path))
                    {
                        File.Move(path, enabledPath, true);
                        item.PluginDllFullPath = enabledPath;
                    }
                }
                item.IsEnabled = true;
                Status($"Enabled: {item.DisplayName}");
            }
            catch (Exception ex)
            {
                Status("Enable failed: " + ex.Message);
            }
            finally
            {
                RefreshMods();
            }
        }

        private void ModToggle_Unchecked(object sender, RoutedEventArgs e)
        {
            if (_isUpdatingList) return;
            var item = (sender as FrameworkElement)?.DataContext as ModItem;
            if (item == null) return;

            try
            {
                var path = item.PluginDllFullPath;
                if (!path.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase))
                {
                    var disabledPath = path + ".disabled";
                    if (File.Exists(path))
                    {
                        File.Move(path, disabledPath, true);
                        item.PluginDllFullPath = disabledPath;
                    }
                }
                item.IsEnabled = false;
                Status($"Disabled: {item.DisplayName}");
            }
            catch (Exception ex)
            {
                Status("Disable failed: " + ex.Message);
            }
            finally
            {
                RefreshMods();
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

        private void Window_Drop(object sender, DragEventArgs e)
        {
            try
            {
                if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
                var paths = (string[])e.Data.GetData(DataFormats.FileDrop);
                foreach (var p in paths) InstallAny(p);
            }
            catch (Exception ex)
            {
                Status("Drop install failed: " + ex.Message);
            }
        }

        private void InstallAny(string path)
        {
            var root = GamePathBox.Text;
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
            {
                Status("Set a valid game folder first.");
                return;
            }

            if (!Directory.Exists(Installer.GetBepInExDir(root)))
            {
                MessageBox.Show(
                    "BepInEx folder not found.\n\n" +
                    "Please install BepInEx 5.x (x64) into the Erenshor folder before installing mods.",
                    "BepInEx not detected",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                Status("Install blocked: BepInEx not installed.");
                return;
            }

            if (!Directory.Exists(Installer.GetPluginsDir(root)))
            {
                MessageBox.Show(
                    "BepInEx\\plugins folder not found.\n\n" +
                    "Run Erenshor once after installing BepInEx to complete setup, then try again.",
                    "Plugins folder missing",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                Status("Install blocked: run Erenshor once to create BepInEx\\plugins.");
                return;
            }

            try { Installer.ValidateBepInExOrThrow(root); }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "BepInEx not detected", MessageBoxButton.OK, MessageBoxImage.Warning);
                Status("Install aborted: " + ex.Message);
                return;
            }

            var cfgStatus = Installer.GetBepInExConfigStatus(root, out var cfgPath);
            if (cfgStatus == Installer.BepInExConfigStatus.MissingConfig)
            {
                MessageBox.Show(
                    "BepInEx.cfg was not found.\n\n" +
                    "You must run Erenshor once after installing BepInEx so it can create the config files and folder structure.",
                    "BepInEx config missing",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                Status("Install blocked: run Erenshor once to generate BepInEx.cfg.");
                return;
            }
            if (cfgStatus == Installer.BepInExConfigStatus.MissingKey ||
                cfgStatus == Installer.BepInExConfigStatus.WrongValue)
            {
                var fix = MessageBox.Show(
                    $"BepInEx.cfg found at:\n{cfgPath}\n\n" +
                    "Setting 'HideManagerGameObject' should be TRUE for Erenshor mods to work correctly.\n" +
                    "Would you like me to set it to true?",
                    "BepInEx config",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (fix == MessageBoxResult.Yes)
                {
                    try { Installer.EnsureHideManagerGameObjectTrue(root); }
                    catch (Exception ex2)
                    {
                        Status("Failed to update config: " + ex2.Message);
                    }
                }
                else
                {
                    Status("Warning: HideManagerGameObject is not true. Some mods may not behave correctly.");
                }
            }

            try
            {
                Installer.InstallResult result;
                if (Directory.Exists(path))
                {
                    result = Installer.InstallFromDirectory(root, path);
                }
                else if (path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                {
                    var plugins = Installer.GetPluginsDir(root);
                    var fileName = Path.GetFileName(path);
                    var target = Path.Combine(plugins, fileName);
                    if (File.Exists(target))
                    {
                        var overwrite = MessageBox.Show(
                            $"'{fileName}' already exists in plugins. Overwrite?",
                            "File exists",
                            MessageBoxButton.YesNo,
                            MessageBoxImage.Question);
                        if (overwrite != MessageBoxResult.Yes)
                        {
                            Status("Install canceled.");
                            return;
                        }
                    }
                    result = Installer.InstallDll(root, path);
                }
                else
                {
                    result = Installer.InstallFromAny(root, path);
                }

                Status($"Installed into: {RelativeToGame(result.TargetDir)}");

                // Scan only for plugin dll; skip dependencies
                VersionScanner.ModVersionInfo? scan = null;

                if (!string.IsNullOrEmpty(result.PrimaryDll) && File.Exists(result.PrimaryDll))
                {
                    scan = VersionScanner.ScanDll(result.PrimaryDll);
                }
                else
                {
                    var plugins = Installer.GetPluginsDir(root);
                    if (!string.Equals(result.TargetDir, plugins, StringComparison.OrdinalIgnoreCase)
                        && Directory.Exists(result.TargetDir))
                    {
                        scan = VersionScanner.ScanFolder(result.TargetDir);
                    }
                    else
                    {
                        if (File.Exists(path))
                        {
                            var installedFile = Path.Combine(plugins, Path.GetFileName(path));
                            if (File.Exists(installedFile))
                                scan = VersionScanner.ScanDll(installedFile);
                        }
                    }
                }

                if (scan != null && !string.IsNullOrWhiteSpace(scan.Guid))
                {
                    ModIndex.UpsertFromScan(root, scan);
                }

                RefreshMods();
            }
            catch (Exception ex)
            {
                Status("Install failed: " + ex.Message);
            }
        }

        private string RelativeToGame(string fullPath)
        {
            var root = GamePathBox.Text;
            return string.IsNullOrWhiteSpace(root) ? fullPath : fullPath.Replace(root, ".").Trim();
        }

        private void Status(string msg) => StatusBlock.Text = msg;

        private void RefreshMods()
        {
            _isUpdatingList = true;
            Mods.Clear();

            var root = GamePathBox.Text;
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) { _isUpdatingList = false; return; }

            var plugins = Installer.GetPluginsDir(root);
            if (!Directory.Exists(plugins))
            {
                Status("BepInEx/plugins not found. Run Erenshor once after installing BepInEx.");
                _isUpdatingList = false;
                return;
            }

            // Ensure index exists at least once (and is minimal)
            ModIndex.EnsureMinimalIndex(root);
            var index = ModIndex.Load(root);

            // Folder mods: include ONLY folders containing a plugin dll
            var dirs = Directory.GetDirectories(plugins).OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase);
            foreach (var dir in dirs)
            {
                VersionScanner.ModVersionInfo? scan = null;
                try { scan = VersionScanner.ScanFolder(dir); } catch { }

                if (scan == null || string.IsNullOrWhiteSpace(scan.Guid)) continue; // dependency-only folder, skip

                var dllPath = scan.DllPath;
                var enabled = !dllPath.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase);

                Mods.Add(new ModItem
                {
                    Guid = scan.Guid,
                    DisplayName = string.IsNullOrWhiteSpace(scan.Name) ? scan.Guid : scan.Name,
                    Version = string.IsNullOrWhiteSpace(scan.Version) ? "0.0.0" : scan.Version,
                    IsFolder = true,
                    FolderName = Path.GetFileName(dir),
                    PluginDllFullPath = dllPath,
                    IsEnabled = enabled
                });
            }

            // Top-level plugin DLLs: consider both enabled and disabled; show only if they’re plugins
            var enabledDlls = Directory.GetFiles(plugins, "*.dll", SearchOption.TopDirectoryOnly).ToArray();
            var disabledDlls = Directory.GetFiles(plugins, "*.dll.disabled", SearchOption.TopDirectoryOnly).ToArray();

            var allBaseNames = enabledDlls.Select(p => Path.GetFileNameWithoutExtension(p))
                                          .Union(disabledDlls.Select(p => Path.GetFileNameWithoutExtension(p)
                                                                                .Replace(".dll", "", StringComparison.OrdinalIgnoreCase)))
                                          .Distinct(StringComparer.OrdinalIgnoreCase)
                                          .OrderBy(n => n, StringComparer.OrdinalIgnoreCase);

            foreach (var baseName in allBaseNames)
            {
                var enabledPath = Path.Combine(plugins, baseName + ".dll");
                var disabledPath = enabledPath + ".disabled";
                var chosenPath = File.Exists(enabledPath) ? enabledPath
                                : (File.Exists(disabledPath) ? disabledPath : null);
                if (chosenPath == null) continue;

                VersionScanner.ModVersionInfo? scan = null;
                try { scan = VersionScanner.ScanDll(chosenPath); } catch { }
                if (scan == null || string.IsNullOrWhiteSpace(scan.Guid)) continue; // dependency DLL: skip

                Mods.Add(new ModItem
                {
                    Guid = scan.Guid,
                    DisplayName = string.IsNullOrWhiteSpace(scan.Name) ? scan.Guid : scan.Name,
                    Version = string.IsNullOrWhiteSpace(scan.Version) ? "0.0.0" : scan.Version,
                    IsFolder = false,
                    DllFileName = baseName + ".dll",
                    PluginDllFullPath = chosenPath,
                    IsEnabled = File.Exists(enabledPath)
                });
            }

            _isUpdatingList = false;
        }
    }
}
