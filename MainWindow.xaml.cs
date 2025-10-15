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
            public string DisplayName { get; set; } = "";
            public bool IsFolder { get; set; }
            public string? FolderName { get; set; }     
            public string? DllFileName { get; set; }      
            public bool IsEnabled { get; set; }
            public bool IsPartial { get; set; }            
            public bool HasDll { get; set; }               
            public string StatusSuffix
            {
                get
                {
                    if (IsFolder)
                    {
                        if (!HasDll) return " (no dll)";
                        if (IsPartial) return " (partial)";
                        return IsEnabled ? "" : " (disabled)";
                    }
                    else
                    {
                        return IsEnabled ? "" : " (disabled)";
                    }
                }
            }
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

        private void DetectSteam_Click(object sender, RoutedEventArgs e) => TryAutoDetect();

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
                var plugins = Installer.GetPluginsDir(GamePathBox.Text);

                if (item.IsFolder)
                {
                    var folderName = item.FolderName!;
                    var target = Path.Combine(plugins, folderName);
                    if (!Directory.Exists(target)) { Status("Folder not found: " + folderName); return; }

                    var confirm = MessageBox.Show(
                        $"Remove mod folder '{folderName}'?",
                        "Confirm Uninstall",
                        MessageBoxButton.YesNo, MessageBoxImage.Question);

                    if (confirm == MessageBoxResult.Yes)
                    {
                        try { Services.Installer.TryDeleteDirectory(target); Status($"Removed: {RelativeToGame(target)}"); RefreshMods(); }
                        catch (Exception ex) { Status("Uninstall failed: " + ex.Message); }
                    }
                }
                else
                {
                    var file = item.DllFileName!;
                    var dllPath = Path.Combine(plugins, file);
                    var dllDisabled = dllPath + ".disabled";
                    var toDelete = File.Exists(dllPath) ? dllPath : dllDisabled;

                    if (!File.Exists(toDelete)) { Status("File not found: " + file); return; }

                    var confirm = MessageBox.Show(
                        $"Remove DLL '{file}' from plugins?",
                        "Confirm Uninstall",
                        MessageBoxButton.YesNo, MessageBoxImage.Question);

                    if (confirm == MessageBoxResult.Yes)
                    {
                        try { File.Delete(toDelete); Status($"Removed: ./BepInEx/plugins/{Path.GetFileName(toDelete)}"); RefreshMods(); }
                        catch (Exception ex) { Status("Uninstall failed: " + ex.Message); }
                    }
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

            var root = GamePathBox.Text;
            try
            {
                if (item.IsFolder)
                {
                    Installer.EnableFolderMod(root, item.FolderName!);
                }
                else
                {
                    Installer.EnableDll(root, item.DllFileName!);
                }
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

            var root = GamePathBox.Text;
            try
            {
                if (item.IsFolder)
                {
                    Installer.DisableFolderMod(root, item.FolderName!);
                }
                else
                {
                    Installer.DisableDll(root, item.DllFileName!);
                }
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
                Services.Installer.InstallResult result;
                if (Directory.Exists(path))
                {
                    result = Services.Installer.InstallFromDirectory(root, path);
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
                    result = Services.Installer.InstallDll(root, path);
                }
                else
                {
                    result = Services.Installer.InstallFromAny(root, path);
                }

                Status($"Installed into: {RelativeToGame(result.TargetDir)}");
                if (!string.IsNullOrEmpty(result.Warning)) Status(result.Warning);
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


            var dirs = Directory.GetDirectories(plugins).OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase);
            foreach (var dir in dirs)
            {
                var (total, disabled) = Installer.GetFolderDllState(dir);
                var item = new ModItem
                {
                    IsFolder = true,
                    FolderName = Path.GetFileName(dir),
                    DisplayName = Path.GetFileName(dir),
                    HasDll = total > 0,
                    IsPartial = (disabled > 0 && disabled < total),
                    IsEnabled = (total == 0) ? true : (disabled == 0)
                };
                Mods.Add(item);
            }
            
            var enabledDlls = Directory.GetFiles(plugins, "*.dll", SearchOption.TopDirectoryOnly)
                                       .Select(Path.GetFileNameWithoutExtension);
            var disabledDlls = Directory.GetFiles(plugins, "*.dll.disabled", SearchOption.TopDirectoryOnly)
                                        .Select(f => Path.GetFileName(f)!.Replace(".dll.disabled", "", StringComparison.OrdinalIgnoreCase));

            var allDllBaseNames = enabledDlls.Union(disabledDlls).Distinct().OrderBy(n => n, StringComparer.OrdinalIgnoreCase);
            foreach (var baseName in allDllBaseNames)
            {
                var isEnabled = File.Exists(Path.Combine(plugins, baseName + ".dll"));
                Mods.Add(new ModItem
                {
                    IsFolder = false,
                    DisplayName = baseName,           
                    DllFileName = baseName + ".dll",
                    IsEnabled = isEnabled
                });
            }

            _isUpdatingList = false;
        }
    }
}
