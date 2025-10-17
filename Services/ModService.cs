using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using ErenshorModInstaller.Wpf.Services.Abstractions;
using ErenshorModInstaller.Wpf.Services.Models;

namespace ErenshorModInstaller.Wpf.Services
{
    /// <summary>
    /// Mod list projection + enable/disable + uninstall + install entry points.
    /// </summary>
    public static class ModService
    {
        public static List<ModItem> ListInstalledMods(string gameRoot)
        {
            var result = new List<ModItem>();

            var plugins = Installer.GetPluginsDir(gameRoot);
            if (!Directory.Exists(plugins)) return result;

            // Folders that contain a plugin dll (skip pure dependency-only folders)
            var dirs = Directory.GetDirectories(plugins).OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase);
            foreach (var dir in dirs)
            {
                VersionScanner.ModVersionInfo? scan = null;
                try { scan = VersionScanner.ScanFolder(dir); } catch { }
                if (scan == null || string.IsNullOrWhiteSpace(scan.Guid)) continue;

                var dllPath = scan.DllPath;
                var enabled = !dllPath.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase);

                result.Add(new ModItem
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

            // Top-level plugin DLLs
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
                if (scan == null || string.IsNullOrWhiteSpace(scan.Guid)) continue;

                result.Add(new ModItem
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

            return result;
        }

        public static void Enable(ModItem item)
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
        }

        public static void Disable(ModItem item)
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
        }

        public static bool Uninstall(string gameRoot, ModItem item, IStatusSink status)
        {
            try
            {
                var plugins = Installer.GetPluginsDir(gameRoot);

                if (item.IsFolder)
                {
                    var target = Path.Combine(plugins, item.FolderName!);
                    if (!Directory.Exists(target)) { status.Warn("Folder not found: " + item.FolderName); return false; }

                    var confirm = MessageBox.Show(
                        $"Remove mod folder '{item.FolderName}'?",
                        "Confirm Uninstall",
                        MessageBoxButton.YesNo, MessageBoxImage.Question);

                    if (confirm != MessageBoxResult.Yes) return false;

                    Installer.TryDeleteDirectory(target);
                    status.Info($"Removed: ./BepInEx/plugins/{item.FolderName}");
                    return true;
                }
                else
                {
                    var path = item.PluginDllFullPath;
                    var baseDll = path.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase)
                        ? path[..^(".disabled".Length)]
                        : path;

                    var existing = File.Exists(baseDll) ? baseDll
                                 : (File.Exists(baseDll + ".disabled") ? baseDll + ".disabled" : null);

                    if (existing == null) { status.Warn("File not found."); return false; }

                    var confirm = MessageBox.Show(
                        $"Remove DLL '{Path.GetFileName(existing)}' from plugins?",
                        "Confirm Uninstall",
                        MessageBoxButton.YesNo, MessageBoxImage.Question);

                    if (confirm != MessageBoxResult.Yes) return false;

                    File.Delete(existing);
                    status.Info($"Removed: ./BepInEx/plugins/{Path.GetFileName(existing)}");
                    return true;
                }
            }
            catch (Exception ex)
            {
                status.Error("Uninstall failed: " + ex.Message);
                return false;
            }
        }

        public static bool InstallAny(string gameRoot, string path, IStatusSink status)
        {
            try
            {
                Installer.InstallResult result;

                if (Directory.Exists(path))
                {
                    result = Installer.InstallFromDirectory(gameRoot, path);
                }
                else if (path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                {
                    var plugins = Installer.GetPluginsDir(gameRoot);
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
                            status.Info("Install canceled.");
                            return false;
                        }
                    }
                    result = Installer.InstallDll(gameRoot, path);
                }
                else
                {
                    result = Installer.InstallFromAny(gameRoot, path);
                }

                status.Info($"Installed into: {RelativeToGame(gameRoot, result.TargetDir)}");

                // Index only the actual plugin dll (skip pure dependencies)
                VersionScanner.ModVersionInfo? scan = null;

                if (!string.IsNullOrEmpty(result.PrimaryDll) && File.Exists(result.PrimaryDll))
                {
                    scan = VersionScanner.ScanDll(result.PrimaryDll);
                }
                else
                {
                    var plugins = Installer.GetPluginsDir(gameRoot);
                    if (!string.Equals(result.TargetDir, plugins, StringComparison.OrdinalIgnoreCase)
                        && Directory.Exists(result.TargetDir))
                    {
                        scan = VersionScanner.ScanFolder(result.TargetDir);
                    }
                    else if (File.Exists(path))
                    {
                        var installedFile = Path.Combine(plugins, Path.GetFileName(path));
                        if (File.Exists(installedFile))
                            scan = VersionScanner.ScanDll(installedFile);
                    }
                }

                if (scan != null && !string.IsNullOrWhiteSpace(scan.Guid))
                {
                    ModIndex.UpsertFromScan(gameRoot, scan);
                }

                if (!string.IsNullOrEmpty(result.Warning))
                    status.Warn(result.Warning);

                return true;
            }
            catch (Exception ex)
            {
                status.Error("Install failed: " + ex.Message);
                return false;
            }
        }

        private static string RelativeToGame(string root, string fullPath)
            => string.IsNullOrWhiteSpace(root) ? fullPath : fullPath.Replace(root, ".").Trim();
    }
}
