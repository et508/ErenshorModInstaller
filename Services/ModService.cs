using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using ErenshorModInstaller.Wpf.Services.Models;

namespace ErenshorModInstaller.Wpf.Services
{
    public static class ModService
    {
        // ---------- Listing (unchanged from your latest behavior) ----------

        public static List<ModItem> ListInstalledMods(string gameRoot)
        {
            var result = new List<ModItem>();
            var plugins = Installer.GetPluginsDir(gameRoot);
            if (!Directory.Exists(plugins)) return result;

            // Folder mods (only if folder contains a plugin dll)
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
            var enabledDlls = Directory.GetFiles(plugins, "*.dll", SearchOption.TopDirectoryOnly);
            var disabledDlls = Directory.GetFiles(plugins, "*.dll.disabled", SearchOption.TopDirectoryOnly);

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

        // ---------- Enable/disable (unchanged) ----------

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

        // ---------- Uninstall (unchanged) ----------

        public static bool Uninstall(string gameRoot, ModItem item, Abstractions.IStatusSink status)
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

        // ---------- Install with version preflight (NEW) ----------

        public static bool InstallAny(string gameRoot, string sourcePath, Abstractions.IStatusSink status)
        {
            try
            {
                // 1) Preflight: figure out incoming mod GUID & Version (if any)
                VersionScanner.ModVersionInfo? incoming = PreflightScan(sourcePath);

                // 2) If we know the GUID, check ModIndex for an existing version and compare
                if (incoming != null && !string.IsNullOrWhiteSpace(incoming.Guid))
                {
                    var index = ModIndex.Load(gameRoot);
                    if (index.TryGetValue(incoming.Guid, out var existing))
                    {
                        var cmp = VersionUtil.Compare(incoming.Version, existing.Version);
                        if (cmp < 0)
                        {
                            var resp = MessageBox.Show(
                                $"You are about to install an OLDER version of '{existing.Name ?? incoming.Name ?? incoming.Guid}'.\n\n" +
                                $"Installed: {existing.Version}\n" +
                                $"Incoming:  {incoming.Version}\n\n" +
                                "This will downgrade the mod. Continue?",
                                "Confirm Downgrade",
                                MessageBoxButton.YesNo,
                                MessageBoxImage.Warning,
                                MessageBoxResult.No);

                            if (resp != MessageBoxResult.Yes)
                            {
                                status.Info("Install canceled (downgrade rejected).");
                                return false;
                            }
                        }
                        else if (cmp == 0)
                        {
                            // Same version: give a gentle confirmation to overwrite files
                            var resp = MessageBox.Show(
                                $"'{existing.Name ?? incoming.Name ?? incoming.Guid}' {existing.Version} is already installed.\n" +
                                "Do you want to reinstall/overwrite?",
                                "Reinstall same version?",
                                MessageBoxButton.YesNo,
                                MessageBoxImage.Question,
                                MessageBoxResult.No);

                            if (resp != MessageBoxResult.Yes)
                            {
                                status.Info("Install canceled.");
                                return false;
                            }
                        }
                        // If cmp > 0 (upgrade), proceed silently (your existing "file exists" prompts will still appear for .dlls)
                    }
                }

                // 3) Perform the actual install (as before)
                Installer.InstallResult result;

                if (Directory.Exists(sourcePath))
                {
                    result = Installer.InstallFromDirectory(gameRoot, sourcePath);
                }
                else if (sourcePath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                {
                    var plugins = Installer.GetPluginsDir(gameRoot);
                    var fileName = Path.GetFileName(sourcePath);
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
                    result = Installer.InstallDll(gameRoot, sourcePath);
                }
                else
                {
                    result = Installer.InstallFromAny(gameRoot, sourcePath);
                }

                status.Info($"Installed into: {RelativeToGame(gameRoot, result.TargetDir)}");

                // 4) Index the actually-installed plugin (skip pure dependencies)
                VersionScanner.ModVersionInfo? finalScan = null;

                if (!string.IsNullOrEmpty(result.PrimaryDll) && File.Exists(result.PrimaryDll))
                {
                    finalScan = VersionScanner.ScanDll(result.PrimaryDll);
                }
                else
                {
                    var plugins = Installer.GetPluginsDir(gameRoot);
                    if (!string.Equals(result.TargetDir, plugins, StringComparison.OrdinalIgnoreCase)
                        && Directory.Exists(result.TargetDir))
                    {
                        finalScan = VersionScanner.ScanFolder(result.TargetDir);
                    }
                    else if (File.Exists(sourcePath))
                    {
                        var installedFile = Path.Combine(plugins, Path.GetFileName(sourcePath));
                        if (File.Exists(installedFile))
                            finalScan = VersionScanner.ScanDll(installedFile);
                    }
                }

                if (finalScan != null && !string.IsNullOrWhiteSpace(finalScan.Guid))
                {
                    ModIndex.UpsertFromScan(gameRoot, finalScan);
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

        private static VersionScanner.ModVersionInfo? PreflightScan(string path)
        {
            try
            {
                if (Directory.Exists(path))
                    return VersionScanner.ScanFolder(path);

                if (path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                    return VersionScanner.ScanDll(path);

                // Archive: peek for the first plugin dll with a BepInPlugin attribute
                if (path.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ||
                    path.EndsWith(".7z", StringComparison.OrdinalIgnoreCase) ||
                    path.EndsWith(".rar", StringComparison.OrdinalIgnoreCase))
                {
                    if (Installer.TryScanArchiveForPlugin(path, out var info))
                        return info;
                }
            }
            catch
            {
                // ignore scan failures — we’ll just proceed without version gating
            }
            return null;
        }

        private static string RelativeToGame(string root, string fullPath)
            => string.IsNullOrWhiteSpace(root) ? fullPath : fullPath.Replace(root, ".").Trim();
    }
}
