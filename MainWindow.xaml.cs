using Microsoft.Win32;
using System;
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
            var ofd = new OpenFileDialog { Filter = "Zip files|*.zip", Title = "Choose a Mod.zip" };
            if (ofd.ShowDialog() == true) InstallZip(ofd.FileName);
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
                e.Effects = files.Any(f => f.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                    ? DragDropEffects.Copy : DragDropEffects.None;
            }
            else e.Effects = DragDropEffects.None;

            e.Handled = true;
        }

        private void Window_Drop(object sender, DragEventArgs e)
        {
            try
            {
                if (!e.Data.GetDataPresent(DataFormats.FileDrop)) return;
                var files = (string[])e.Data.GetData(DataFormats.FileDrop);
                foreach (var f in files.Where(f => f.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)))
                    InstallZip(f);
            }
            catch (Exception ex)
            {
                Status("Drop install failed: " + ex.Message);
            }
        }

        // Helpers
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
            {
                ModsList.Items.Add(Path.GetFileName(dir));
            }
        }

        private void InstallZip(string zipPath)
        {
            try
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

                var result = Installer.InstallZip(root, zipPath);
                Status($"Installed into: {RelativeToGame(result.TargetDir)}");
                if (!string.IsNullOrEmpty(result.Warning)) Status(result.Warning);
                RefreshMods();
            }
            catch (Exception ex)
            {
                Status("Install failed: " + ex.Message);
            }
        }
    }
}
