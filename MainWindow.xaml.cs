using Microsoft.Win32;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using ErenshorModInstaller.Wpf.Services;

namespace ErenshorModInstaller.Wpf
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
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
                        {
                            var text = "BepInEx.cfg not found. Run Erenshor once to let BepInEx generate its configs.";
                            Status(text);
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
                        }

                    case Installer.BepInExConfigStatus.MissingKey:
                    case Installer.BepInExConfigStatus.WrongValue:
                        {
                            var warn = "Warning: HideManagerGameObject should be TRUE for Erenshor mods to work correctly.";
                            Status(warn);

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

            if (ModsList.SelectedItem is string folderName)
            {
                var plugins = Installer.GetPluginsDir(GamePathBox.Text);
                var target = Path.Combine(plugins, folderName);
                if (!Directory.Exists(target)) { Status("Folder not found: " + folderName); return; }

                var confirm = MessageBox.Show(
                    $"Remove mod folder '{folderName}'?",
                    "Confirm Uninstall",
                    MessageBoxButton.YesNo, MessageBoxImage.Question);

                if (confirm == MessageBoxResult.Yes)
                {
                    try { Installer.TryDeleteDirectory(target); Status($"Removed: {RelativeToGame(target)}"); RefreshMods(); }
                    catch (Exception ex) { Status("Uninstall failed: " + ex.Message); }
                }
            }
            else
            {
                Status("Select a mod folder to uninstall.");
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
                    path.EndsWith(".rar", StringComparison.OrdinalIgnoreCase));
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
                    result = Services.Installer.InstallFromDirectory(root, path);
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
            ModsList.Items.Clear();
            var root = GamePathBox.Text;
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) return;

            var plugins = Installer.GetPluginsDir(root);
            if (!Directory.Exists(plugins))
            {
                Status("BepInEx/plugins not found. Run Erenshor once after installing BepInEx.");
                return;
            }

            foreach (var dir in Directory.GetDirectories(plugins))
                ModsList.Items.Add(Path.GetFileName(dir));
        }
    }
}
