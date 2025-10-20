using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using ErenshorModInstaller.Wpf.Services.Abstractions;
using ErenshorModInstaller.Wpf.Services.Models;
using ErenshorModInstaller.Wpf.UI;

namespace ErenshorModInstaller.Wpf.Services
{
    public static class ModService
    {
        public sealed class AlternateVersion
        {
            public string Version { get; init; } = "0.0.0";
            public string Label { get; init; } = "";
            public string StoredPath { get; init; } = "";
            public bool IsFolderPackage { get; init; }
            public string? DllFileName { get; init; }
            public string? FolderName { get; init; }
        }

        public static List<ModItem> ListInstalledMods(string root)
        {
            var list = new List<ModItem>();
            var plugins = Installer.GetPluginsDir(root);
            if (!Directory.Exists(plugins)) return list;
            
            var dirs = Directory.GetDirectories(plugins)
                                .Where(d => !IsHiddenOrDotFolder(d))
                                .OrderBy(Path.GetFileName, StringComparer.OrdinalIgnoreCase);

            foreach (var dir in dirs)
            {
                VersionScanner.ModVersionInfo? scan = null;
                try { scan = VersionScanner.ScanFolder(dir); } catch { }
                if (scan == null || string.IsNullOrWhiteSpace(scan.Guid)) continue;

                var enabled = !scan.DllPath.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase);

                list.Add(new ModItem
                {
                    Guid = scan.Guid,
                    DisplayName = string.IsNullOrWhiteSpace(scan.Name) ? scan.Guid : scan.Name,
                    Version = string.IsNullOrWhiteSpace(scan.Version) ? "0.0.0" : scan.Version,
                    IsFolder = true,
                    FolderName = Path.GetFileName(dir),
                    PluginDllFullPath = scan.DllPath,
                    IsEnabled = enabled
                });
            }
            
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

                list.Add(new ModItem
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

            return list;
        }

        public static void Enable(ModItem item)
        {
            if (item == null) return;
            var path = item.PluginDllFullPath;
            if (string.IsNullOrWhiteSpace(path)) return;

            if (item.IsFolder)
            {
                var folder = Path.GetDirectoryName(path);
                if (folder != null && Directory.Exists(folder))
                    EnableAllDllsRecursively(folder);
                item.IsEnabled = true;
                return;
            }

            if (path.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase))
            {
                var enabledPath = path[..^(".disabled".Length)];
                if (File.Exists(path))
                {
                    if (File.Exists(enabledPath)) File.Delete(enabledPath);
                    File.Move(path, enabledPath);
                    item.PluginDllFullPath = enabledPath;
                }
            }
            item.IsEnabled = true;
        }

        public static void Disable(ModItem item)
        {
            if (item == null) return;
            var path = item.PluginDllFullPath;
            if (string.IsNullOrWhiteSpace(path)) return;

            if (item.IsFolder)
            {
                var folder = Path.GetDirectoryName(path);
                if (folder != null && Directory.Exists(folder))
                    DisableAllDllsRecursively(folder);
                item.IsEnabled = false;
                return;
            }

            if (!path.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase))
            {
                var disabledPath = path + ".disabled";
                if (File.Exists(path))
                {
                    if (File.Exists(disabledPath)) File.Delete(disabledPath);
                    File.Move(path, disabledPath);
                    item.PluginDllFullPath = disabledPath;
                }
            }
            item.IsEnabled = false;
        }
        
        public static bool Uninstall(string root, ModItem item, IStatusSink sink)
        {
            try
            {
                var plugins = Installer.GetPluginsDir(root);
                var guid = item.Guid;
                var name = item.DisplayName;
                var storeRoot = VersionStoreForGuid(root, guid);
                var versions = Directory.Exists(storeRoot)
                    ? Directory.GetDirectories(storeRoot).Select(Path.GetFileName)!.ToList()
                    : new List<string>();
                
                if (versions.Count == 0)
                {
                    var confirm = Prompts.ShowConfirmUninstallSingle(Application.Current?.MainWindow!, item.DisplayName);
                    if (confirm != PromptResult.Primary) return false;

                    if (item.IsFolder)
                    {
                        var target = Path.Combine(plugins, item.FolderName!);
                        if (!Directory.Exists(target)) { sink.Warn("Folder not found: " + item.FolderName); return false; }

                        TryDeleteDirectory(target);
                        sink.Info($"Removed: ./BepInEx/plugins/{item.FolderName}");
                        return true;
                    }
                    else
                    {
                        var baseDll = Path.Combine(plugins, item.DllFileName!);
                        var dllPath = File.Exists(baseDll) ? baseDll
                            : (File.Exists(baseDll + ".disabled") ? baseDll + ".disabled" : null);
                        if (dllPath == null) { sink.Warn("File not found: " + item.DllFileName); return false; }

                        File.Delete(dllPath);
                        sink.Info($"Removed: ./BepInEx/plugins/{Path.GetFileName(dllPath)}");
                        return true;
                    }
                }
                
                var res = Prompts.ShowConfirmUninstallMulti(Application.Current?.MainWindow!, item.DisplayName, item.Version, versions);
                if (res == PromptResult.Cancel) return false;

                if (res == PromptResult.Primary)
                {
                    if (item.IsFolder)
                    {
                        var target = Path.Combine(plugins, item.FolderName!);
                        TryDeleteDirectory(target);
                    }
                    else
                    {
                        var baseDll = Path.Combine(plugins, item.DllFileName!);
                        var dllPath = File.Exists(baseDll) ? baseDll
                                    : (File.Exists(baseDll + ".disabled") ? baseDll + ".disabled" : null);
                        if (dllPath != null) File.Delete(dllPath);
                    }
                    
                    TryDeleteDirectory(storeRoot);
                    sink.Info($"Removed all versions of {item.DisplayName}.");
                    return true;
                }
                
                var picker = new VersionUninstallDialog(item.DisplayName, item.Version, versions)
                {
                    Owner = Application.Current?.MainWindow
                };
                if (picker.ShowDialog() != true) return false;

                var removeActive = picker.RemoveActive;
                var removeStored = new HashSet<string>(picker.StoredVersionsToRemove, StringComparer.OrdinalIgnoreCase);
                var keepAsActive = picker.KeepAsActiveVersion; 
                
                bool switchedToStored = false;

                if (!string.IsNullOrWhiteSpace(keepAsActive) &&
                    (!string.Equals(keepAsActive, item.Version, StringComparison.OrdinalIgnoreCase) || removeActive))
                {
                    var choice = FindStoredChoice(root, item, keepAsActive);
                    if (choice != null)
                    {
                        if (item.IsFolder)
                        {
                            var activeFolder = Path.Combine(plugins, item.FolderName ?? "");
                            TryDeleteDirectory(activeFolder);
                            MoveDirectoryRobust(choice.StoredPath, activeFolder);
                            EnableAllDllsRecursively(activeFolder);
                        }
                        else
                        {
                            var dllName = item.DllFileName ?? Path.GetFileName(item.PluginDllFullPath) ?? "plugin.dll";
                            var activePath = Path.Combine(plugins, dllName);
                            var activeDisabled = activePath + ".disabled";
                            if (File.Exists(activePath)) File.Delete(activePath);
                            if (File.Exists(activeDisabled)) File.Delete(activeDisabled);
                            
                            if (choice.StoredPath.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase))
                            {
                                if (File.Exists(activePath)) File.Delete(activePath);
                                File.Move(choice.StoredPath, activePath);
                            }
                            else
                            {
                                if (File.Exists(activePath)) File.Delete(activePath);
                                File.Move(choice.StoredPath, activePath);
                            }
                        }

                        switchedToStored = true;
                        removeActive = false;
                        removeStored.Remove(keepAsActive);
                        CleanupIfEmpty(Path.Combine(storeRoot, keepAsActive));
                    }
                }
                
                foreach (var v in removeStored)
                {
                    var vDir = Path.Combine(storeRoot, v);
                    TryDeleteDirectory(vDir);
                }
                
                if (removeActive && !switchedToStored)
                {
                    if (item.IsFolder)
                    {
                        var target = Path.Combine(plugins, item.FolderName!);
                        TryDeleteDirectory(target);
                    }
                    else
                    {
                        var baseDll = Path.Combine(plugins, item.DllFileName!);
                        var dllPath = File.Exists(baseDll) ? baseDll
                                    : (File.Exists(baseDll + ".disabled") ? baseDll + ".disabled" : null);
                        if (dllPath != null) File.Delete(dllPath);
                    }
                }
                
                string? keptActiveVersion = null;
                if (switchedToStored && !string.IsNullOrWhiteSpace(keepAsActive))
                    keptActiveVersion = keepAsActive;
                else if (!removeActive)
                    keptActiveVersion = item.Version;

                if (Directory.Exists(storeRoot))
                {
                    var remaining = Directory.GetDirectories(storeRoot)
                                             .Select(Path.GetFileName)
                                             .Where(n => !string.IsNullOrWhiteSpace(n))
                                             .ToList();

                    if (remaining.Count == 0)
                    {
                        TryDeleteDirectory(storeRoot);
                    }
                    else if (keptActiveVersion != null &&
                             remaining.Count == 1 &&
                             string.Equals(remaining[0], keptActiveVersion, StringComparison.OrdinalIgnoreCase))
                    {
                        var onlyDir = Path.Combine(storeRoot, remaining[0]!);
                        TryDeleteDirectory(onlyDir);
                        CleanupIfEmpty(storeRoot);
                    }
                }



                TryHide(Path.Combine(plugins, ".versions"));
                TryHide(storeRoot);

                sink.Info("Uninstall operation completed.");
                return true;
            }
            catch (Exception ex)
            {
                sink.Error("Uninstall failed: " + ex.Message);
                return false;
            }
        }
        

        public static bool InstallAny(string root, string path, IStatusSink sink)
        {
            try
            {
                VersionScanner.ModVersionInfo? incoming = null;
                bool isArchive = IsArchive(path);
                if (File.Exists(path))
                {
                    if (path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                        incoming = VersionScanner.ScanDll(path);
                    else if (isArchive)
                        Installer.TryScanArchiveForPlugin(path, out incoming);
                }
                else if (Directory.Exists(path))
                {
                    incoming = VersionScanner.ScanFolder(path);
                }

                if (incoming != null && !string.IsNullOrWhiteSpace(incoming.Guid))
                {
                    var installed = FindInstalledByGuid(root, incoming.Guid);
                    if (installed != null)
                    {
                        var cmp = CompareSemVerSafe(incoming.Version, installed.Version);
                        if (cmp < 0)
                        {
                            var res = Prompts.ShowDowngrade(Application.Current?.MainWindow!,
                                installed.DisplayName, installed.Version, incoming.Version);

                            if (res == PromptResult.Cancel)
                            {
                                sink.Info("Install canceled.");
                                return false;
                            }
                            if (res == PromptResult.Secondary) 
                            {
                                StashIncomingOnly_Move(root, installed, path, incoming, sink);
                                sink.Info($"Stored alternate version {incoming.Version} for {installed.DisplayName}. Right-click the mod to switch.");
                                return true;
                            }
                            StashCurrentActive_Move(root, installed, sink);
                        }
                        else if (cmp > 0)
                        {
                            StashCurrentActive_Move(root, installed, sink);
                        }
                        // cmp == 0 => same version, allow overwrite without stashing
                    }
                }

                Installer.InstallResult result;
                if (Directory.Exists(path))
                    result = Installer.InstallFromDirectory(root, path);
                else if (path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                    result = Installer.InstallDll(root, path);
                else if (isArchive)
                    result = Installer.InstallFromAny(root, path);
                else
                    throw new InvalidOperationException("Unsupported file type for install: " + Path.GetFileName(path));

                VersionScanner.ModVersionInfo? scan = null;
                if (!string.IsNullOrWhiteSpace(result.PrimaryDll) && File.Exists(result.PrimaryDll))
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
                    else if (File.Exists(path))
                    {
                        var installedFile = Path.Combine(plugins, Path.GetFileName(path));
                        if (File.Exists(installedFile))
                            scan = VersionScanner.ScanDll(installedFile);
                    }
                }

                if (scan != null && !string.IsNullOrWhiteSpace(scan.Guid))
                {
                    ModIndex.UpsertFromScan(root, scan);
                }

                sink.Info($"Installed into: {RelativeToGame(root, result.TargetDir)}");
                if (!string.IsNullOrEmpty(result.Warning)) sink.Warn(result.Warning);
                return true;
            }
            catch (Exception ex)
            {
                sink.Error("Install failed: " + ex.Message);
                return false;
            }
        }

        public static List<AlternateVersion> GetAlternateVersions(string root, ModItem item)
        {
            var result = new List<AlternateVersion>();
            try
            {
                if (item == null || string.IsNullOrWhiteSpace(item.Guid)) return result;

                var storeRoot = VersionStoreForGuid(root, item.Guid);
                if (!Directory.Exists(storeRoot)) return result;

                foreach (var versionDir in Directory.GetDirectories(storeRoot))
                {
                    var version = Path.GetFileName(versionDir) ?? "0.0.0";
                    if (string.Equals(version, item.Version, StringComparison.OrdinalIgnoreCase))
                        continue;

                    if (item.IsFolder)
                    {
                        var storedFolder = Path.Combine(versionDir, item.FolderName ?? "");
                        var exists = Directory.Exists(storedFolder) ? storedFolder : versionDir;

                        result.Add(new AlternateVersion
                        {
                            Version = version,
                            Label = version,
                            StoredPath = exists,
                            IsFolderPackage = true,
                            FolderName = item.FolderName
                        });
                    }
                    else
                    {
                        var exactDll = Path.Combine(versionDir, item.DllFileName ?? "plugin.dll");
                        var candidate = File.Exists(exactDll) ? exactDll
                                      : (File.Exists(exactDll + ".disabled") ? exactDll + ".disabled" : null);

                        if (candidate == null)
                        {
                            candidate = Directory.GetFiles(versionDir, "*.dll*", SearchOption.TopDirectoryOnly)
                                                 .FirstOrDefault();
                        }

                        if (!string.IsNullOrWhiteSpace(candidate) && File.Exists(candidate))
                        {
                            result.Add(new AlternateVersion
                            {
                                Version = version,
                                Label = version,
                                StoredPath = candidate,
                                IsFolderPackage = false,
                                DllFileName = Path.GetFileName(candidate)
                            });
                        }
                    }
                }

                result = result
                    .OrderByDescending(a => a.Version, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(a => a.Label, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
            catch { }

            return result;
        }

        public static bool SwitchToVersion(string root, ModItem item, AlternateVersion choice, IStatusSink sink)
        {
            try
            {
                StashCurrentActive_Move(root, item, sink);

                var plugins = Installer.GetPluginsDir(root);

                if (item.IsFolder)
                {
                    var activeFolder = Path.Combine(plugins, item.FolderName ?? "");
                    TryDeleteDirectory(activeFolder);

                    if (!Directory.Exists(choice.StoredPath))
                        throw new InvalidOperationException("Stored version payload not found: " + choice.StoredPath);
                    
                    MoveDirectoryRobust(choice.StoredPath, activeFolder);
                    
                    EnableAllDllsRecursively(activeFolder);

                    sink.Info($"Switched {item.DisplayName} to v{choice.Version}.");
                }
                else
                {
                    var dllName = item.DllFileName ?? Path.GetFileName(item.PluginDllFullPath) ?? "plugin.dll";
                    var activePath = Path.Combine(plugins, dllName);
                    var activeDisabled = activePath + ".disabled";
                    if (File.Exists(activePath)) File.Delete(activePath);
                    if (File.Exists(activeDisabled)) File.Delete(activeDisabled);

                    if (!File.Exists(choice.StoredPath))
                        throw new InvalidOperationException("Stored version file not found: " + choice.StoredPath);
                    
                    if (choice.StoredPath.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase))
                    {
                        if (File.Exists(activePath)) File.Delete(activePath);
                        File.Move(choice.StoredPath, activePath);
                    }
                    else
                    {
                        if (File.Exists(activePath)) File.Delete(activePath);
                        File.Move(choice.StoredPath, activePath);
                    }

                    sink.Info($"Switched {item.DisplayName} to v{choice.Version}.");
                }
                
                TryHide(Path.Combine(plugins, ".versions"));
                TryHide(VersionStoreForGuid(root, item.Guid));

                return true;
            }
            catch (Exception ex)
            {
                sink.Error("Switch failed: " + ex.Message);
                return false;
            }
        }

      

        private static bool IsHiddenOrDotFolder(string path)
        {
            try
            {
                var name = Path.GetFileName(path);
                if (!string.IsNullOrEmpty(name) && name.StartsWith(".", StringComparison.Ordinal)) return true;

                var attr = File.GetAttributes(path);
                return (attr & FileAttributes.Hidden) != 0;
            }
            catch { return false; }
        }

        private static string PluginsDir(string root) => Installer.GetPluginsDir(root);

        private static string VersionStoreForGuid(string root, string guid)
            => Path.Combine(PluginsDir(root), ".versions", SanitizeGuid(guid));

        private static string SanitizeGuid(string guid)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var cleaned = new string(guid.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
            return cleaned;
        }

        private static void TryHide(string path)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    var a = File.GetAttributes(path);
                    if ((a & FileAttributes.Hidden) == 0)
                        File.SetAttributes(path, a | FileAttributes.Hidden);
                }
            }
            catch { }
        }
        
        private static void StashCurrentActive_Move(string root, ModItem installed, IStatusSink? sink)
        {
            try
            {
                var storeRoot = VersionStoreForGuid(root, installed.Guid);
                var version = string.IsNullOrWhiteSpace(installed.Version) ? "0.0.0" : installed.Version;
                var dest = Path.Combine(storeRoot, version);

                Directory.CreateDirectory(dest);
                TryHide(Path.Combine(PluginsDir(root), ".versions"));
                TryHide(storeRoot);

                if (installed.IsFolder)
                {
                    var activeFolder = Path.Combine(PluginsDir(root), installed.FolderName ?? "");
                    if (Directory.Exists(activeFolder))
                    {
                        var destFolder = Path.Combine(dest, installed.FolderName ?? "");
                      
                        TryDeleteDirectory(destFolder);
                        MoveDirectoryRobust(activeFolder, destFolder);

                       
                        DisableAllDllsRecursively(destFolder);
                    }
                }
                else
                {
                    var dllName = Path.GetFileName(installed.PluginDllFullPath) ?? "plugin.dll";
                    var enabledPath = Path.Combine(PluginsDir(root), dllName);
                    var disabledPath = enabledPath + ".disabled";
                    var existing = File.Exists(enabledPath) ? enabledPath
                                   : (File.Exists(disabledPath) ? disabledPath : null);
                    if (existing != null)
                    {
                        Directory.CreateDirectory(dest);
                        var destDisabled = Path.Combine(dest, Path.GetFileName(enabledPath) + ".disabled");
                        
                        if (existing.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase))
                        {
                            if (File.Exists(destDisabled)) File.Delete(destDisabled);
                            File.Move(existing, destDisabled);
                        }
                        else
                        {
                            var temp = Path.Combine(dest, Path.GetFileName(enabledPath));
                            if (File.Exists(temp)) File.Delete(temp);
                            File.Move(existing, temp);
                            if (File.Exists(destDisabled)) File.Delete(destDisabled);
                            File.Move(temp, destDisabled);
                        }
                    }
                }

                TryHide(dest);
            }
            catch (Exception ex)
            {
                sink?.Warn("Could not snapshot current version: " + ex.Message);
            }
        }
        
        private static void StashIncomingOnly_Move(string root, ModItem installed, string incomingPath, VersionScanner.ModVersionInfo incoming, IStatusSink sink)
        {
            try
            {
                var storeRoot = VersionStoreForGuid(root, installed.Guid);
                var destVersion = Path.Combine(storeRoot, incoming.Version);
                Directory.CreateDirectory(destVersion);
                TryHide(Path.Combine(PluginsDir(root), ".versions"));
                TryHide(storeRoot);

                if (Directory.Exists(incomingPath))
                {
                    var destFolder = Path.Combine(destVersion, installed.FolderName ?? Path.GetFileName(incomingPath));
                    TryDeleteDirectory(destFolder);
                    MoveDirectoryRobust(incomingPath, destFolder);
                    DisableAllDllsRecursively(destFolder);
                }
                else if (incomingPath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                {
                    Directory.CreateDirectory(destVersion);
                    var destDisabled = Path.Combine(destVersion, Path.GetFileName(incomingPath) + ".disabled");
                    if (File.Exists(destDisabled)) File.Delete(destDisabled);
                    File.Move(incomingPath, destDisabled);
                }
                else if (IsArchive(incomingPath))
                {
                    var tmpRoot = Path.Combine(Path.GetTempPath(), "Erenshor_Alt_" + Guid.NewGuid().ToString("N"));
                    Directory.CreateDirectory(tmpRoot);
                    try
                    {
                        var bepinex = Path.Combine(tmpRoot, "BepInEx");
                        var plugins = Path.Combine(bepinex, "plugins");
                        Directory.CreateDirectory(plugins);

                        Installer.InstallFromAny(tmpRoot, incomingPath);
                        
                        foreach (var entry in Directory.EnumerateFileSystemEntries(plugins, "*", SearchOption.TopDirectoryOnly))
                        {
                            var name = Path.GetFileName(entry);
                            var target = Path.Combine(destVersion, name!);
                            TryDeleteDirectory(target);
                            if (Directory.Exists(entry))
                                MoveDirectoryRobust(entry, target);
                            else
                            {
                                if (File.Exists(target)) File.Delete(target);
                                File.Move(entry, target);
                            }
                        }

                        DisableAllDllsRecursively(destVersion);
                    }
                    finally
                    {
                        try { Directory.Delete(tmpRoot, true); } catch { }
                    }
                }

                TryHide(destVersion);
            }
            catch (Exception ex)
            {
                sink.Warn("Failed to store alternate version: " + ex.Message);
            }
        }

        private static ModItem? FindInstalledByGuid(string root, string guid)
        {
            return ListInstalledMods(root).FirstOrDefault(m => string.Equals(m.Guid, guid, StringComparison.OrdinalIgnoreCase));
        }

        private static int CompareSemVerSafe(string a, string b)
        {
            string norm(string s) => string.IsNullOrWhiteSpace(s) ? "0.0.0" : s.Trim();

            var asv = norm(a).Split(new[] { '.', '-', '+' }, StringSplitOptions.RemoveEmptyEntries);
            var bsv = norm(b).Split(new[] { '.', '-', '+' }, StringSplitOptions.RemoveEmptyEntries);

            int n = Math.Max(asv.Length, bsv.Length);
            for (int i = 0; i < n; i++)
            {
                var pa = i < asv.Length ? asv[i] : "0";
                var pb = i < bsv.Length ? bsv[i] : "0";

                if (int.TryParse(pa, out var ia) && int.TryParse(pb, out var ib))
                {
                    if (ia != ib) return ia.CompareTo(ib);
                    continue;
                }

                int cmp = string.Compare(pa, pb, StringComparison.OrdinalIgnoreCase);
                if (cmp != 0) return cmp;
            }
            return 0;
        }

        private static string RelativeToGame(string root, string fullPath)
            => string.IsNullOrWhiteSpace(root) ? fullPath : fullPath.Replace(root, ".").Trim();

        private static bool IsArchive(string path)
        {
            var ext = Path.GetExtension(path)?.ToLowerInvariant();
            return ext == ".zip" || ext == ".7z" || ext == ".rar";
        }
        
        private static void MoveDirectoryRobust(string sourceDir, string destDir)
        {
            if (!Directory.Exists(sourceDir))
                throw new DirectoryNotFoundException("Source folder not found: " + sourceDir);
            
            TryDeleteDirectory(destDir);
            Directory.CreateDirectory(Path.GetDirectoryName(destDir)!);

            try
            {
                Directory.Move(sourceDir, destDir);
            }
            catch
            {
                CopyDirectory(sourceDir, destDir);
                TryDeleteDirectory(sourceDir);
            }
        }

        private static void CopyDirectory(string sourceDir, string destDir)
        {
            Directory.CreateDirectory(destDir);

            foreach (var file in Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories))
            {
                var rel = Path.GetRelativePath(sourceDir, file);
                var target = Path.Combine(destDir, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(file, target, overwrite: true);
            }
        }

        private static void TryDeleteDirectory(string path)
        {
            if (!Directory.Exists(path)) return;

            foreach (var f in Directory.GetFiles(path, "*", SearchOption.AllDirectories))
            {
                try
                {
                    var attr = File.GetAttributes(f);
                    if ((attr & FileAttributes.ReadOnly) != 0 || (attr & FileAttributes.Hidden) != 0)
                    {
                        File.SetAttributes(f, attr & ~FileAttributes.ReadOnly & ~FileAttributes.Hidden);
                    }
                }
                catch { }
            }

            try { Directory.Delete(path, recursive: true); }
            catch
            {
                try
                {
                    var tmp = path + ".old_" + DateTime.UtcNow.Ticks;
                    Directory.Move(path, tmp);
                }
                catch { }
            }
        }
        
        private static void DisableAllDllsRecursively(string rootDir)
        {
            try
            {
                foreach (var dll in Directory.GetFiles(rootDir, "*.dll", SearchOption.AllDirectories))
                {
                    var disabled = dll + ".disabled";
                    try
                    {
                        if (File.Exists(disabled)) { File.Delete(dll); continue; }
                        File.Move(dll, disabled);
                    }
                    catch { /* ignore per-file errors */ }
                }
            }
            catch { }
        }
        
        private static void EnableAllDllsRecursively(string rootDir)
        {
            try
            {
                foreach (var disabled in Directory.GetFiles(rootDir, "*.dll.disabled", SearchOption.AllDirectories))
                {
                    var enabled = disabled[..^(".disabled".Length)];
                    try
                    {
                        if (File.Exists(enabled)) File.Delete(enabled);
                        File.Move(disabled, enabled);
                    }
                    catch { /* ignore per-file errors */ }
                }
            }
            catch { }
        }

        private static AlternateVersion? FindStoredChoice(string root, ModItem item, string version)
        {
            return GetAlternateVersions(root, item).FirstOrDefault(a =>
                string.Equals(a.Version, version, StringComparison.OrdinalIgnoreCase));
        }

        private static void CleanupIfEmpty(string dir)
        {
            try
            {
                if (!Directory.Exists(dir)) return;
                if (!Directory.EnumerateFileSystemEntries(dir).Any())
                    Directory.Delete(dir, false);
            }
            catch { }
        }
    }
}
