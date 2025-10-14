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
            Directory.CreateDirectory(plugins);
            Process.Start(new ProcessStartInfo { FileName = plugins, UseShellExecute = true });
        }

        private void Validate_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var root = GamePathBox.Text;
                if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
                {
                    Status("Set a valid game folder first.");
                    return;
                }
                var ver = Installer.ValidateBepInExOrThrow(root);
                Status($"BepInEx OK ({ver ?? "version unknown"}).");
            }
            catch (Exception ex)
            {
                Status("BepInEx validation failed: " + ex.Message);
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

        // Drag & Drop
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

        // Helpers
        private void InstallAny(string path)
        {
            var root = GamePathBox.Text;
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
            {
                Status("Set a valid game folder first.");
                return;
            }

            try { Installer.ValidateBepInExOrThrow(root); }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "BepInEx not detected",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                Status("Install aborted. Please install BepInEx 5.x first.");
                return;
            }

            try
            {
                Installer.InstallResult result;
                if (Directory.Exists(path))
                {
                    result = Installer.InstallFromDirectory(root, path);
                }
                else
                {
                    result = Installer.InstallFromAny(root, path);
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

        private void Status(string msg) => StatusBlock.Text = msg;

        private string RelativeToGame(string fullPath)
        {
            var root = GamePathBox.Text;
            return string.IsNullOrWhiteSpace(root) ? fullPath : fullPath.Replace(root, ".").Trim();
        }

        private void RefreshMods()
        {
            ModsList.Items.Clear();
            var root = GamePathBox.Text;
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root)) return;

            var plugins = Installer.GetPluginsDir(root);
            Directory.CreateDirectory(plugins);
            foreach (var dir in Directory.GetDirectories(plugins))
                ModsList.Items.Add(Path.GetFileName(dir));
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

                    // 🟢 close the installer after launching
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


    }
}
