using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using SharpCompress.Common;
using SharpCompress.Readers;

namespace ErenshorModInstaller.Wpf.Services
{
    public static class Installer
    {
        public sealed class InstallResult
        {
            public string TargetDir { get; set; } = ""; 
            public string PrimaryDll { get; set; } = ""; 
            public string Warning { get; set; } = "";  
        }

        public enum BepInExConfigStatus
        {
            Ok,
            MissingConfig,
            MissingKey,
            WrongValue
        }
        
        public static string? ValidateBepInExOrThrow(string gameRoot)
        {
            if (string.IsNullOrWhiteSpace(gameRoot) || !Directory.Exists(gameRoot))
                throw new InvalidOperationException("Game folder is not set or does not exist.");

            var bepDir = GetBepInExDir(gameRoot);
            if (!Directory.Exists(bepDir))
                throw new InvalidOperationException("BepInEx folder not found in the game directory.");

            var core = Path.Combine(bepDir, "core");
            var preloader = Path.Combine(core, "BepInEx.Preloader.dll");
            var bepinexDll = Path.Combine(core, "BepInEx.dll");
            var winhttp = Path.Combine(gameRoot, "winhttp.dll");
            var doorstop = Path.Combine(gameRoot, "doorstop_config.ini");

            if (!File.Exists(winhttp))
                throw new InvalidOperationException("winhttp.dll was not found in the game directory (Doorstop not present).");
            if (!File.Exists(doorstop))
                throw new InvalidOperationException("doorstop_config.ini was not found in the game directory.");
            if (!Directory.Exists(core))
                throw new InvalidOperationException("BepInEx/core folder not found.");
            if (!File.Exists(preloader) && !File.Exists(bepinexDll))
                throw new InvalidOperationException("BepInEx core libraries not found in BepInEx/core.");
            
            try
            {
                var dllForVersion = File.Exists(bepinexDll) ? bepinexDll : preloader;
                if (File.Exists(dllForVersion))
                {
                    var verInfo = FileVersionInfo.GetVersionInfo(dllForVersion);
                    var ver = verInfo?.ProductVersion ?? verInfo?.FileVersion;
                    if (!string.IsNullOrWhiteSpace(ver))
                    {
                        ver = ver.Trim();
                        var plus = ver.IndexOf('+');
                        if (plus >= 0) ver = ver.Substring(0, plus).Trim();
                    }
                    return ver;
                }
            }
            catch { /* ignore */ }

            return null;
        }

        public static string GetBepInExDir(string gameRoot) => Path.Combine(gameRoot, "BepInEx");

        public static string GetPluginsDir(string gameRoot) => Path.Combine(GetBepInExDir(gameRoot), "plugins");
        
        public static BepInExConfigStatus GetBepInExConfigStatus(string gameRoot, out string cfgPath)
        {
            cfgPath = Path.Combine(GetBepInExDir(gameRoot), "config", "BepInEx.cfg");
            if (!File.Exists(cfgPath)) return BepInExConfigStatus.MissingConfig;
            
            var (exists, isTrue) = TryReadHideManagerGameObject(cfgPath);
            if (!exists) return BepInExConfigStatus.MissingKey;
            if (!isTrue) return BepInExConfigStatus.WrongValue;
            return BepInExConfigStatus.Ok;
        }
        
        public static void EnsureHideManagerGameObjectTrue(string gameRoot)
        {
            var cfgPath = Path.Combine(GetBepInExDir(gameRoot), "config", "BepInEx.cfg");
            if (!File.Exists(cfgPath))
                throw new FileNotFoundException("BepInEx.cfg not found. Launch the game once to generate configs.", cfgPath);

            var text = File.ReadAllText(cfgPath);
            var updated = SetHideManagerKeyToTrue(text);
            if (!ReferenceEquals(updated, text))
            {
                File.WriteAllText(cfgPath, updated, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            }
            else
            {
                var appended = AppendHideManagerKey(text);
                File.WriteAllText(cfgPath, appended, new UTF8Encoding(false));
            }
        }
        
        public static InstallResult InstallFromDirectory(string gameRoot, string sourceDir)
        {
            if (string.IsNullOrWhiteSpace(sourceDir) || !Directory.Exists(sourceDir))
                throw new DirectoryNotFoundException("Source folder not found: " + sourceDir);

            var plugins = GetPluginsDir(gameRoot);
            var folderName = Path.GetFileName(sourceDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))!;
            var dest = Path.Combine(plugins, folderName);

            CopyDirectory(sourceDir, dest);

            var primaryDll = FindPrimaryPluginDll(dest);
            return new InstallResult
            {
                TargetDir = dest,
                PrimaryDll = primaryDll ?? ""
            };
        }
        
        public static InstallResult InstallDll(string gameRoot, string dllPath)
        {
            if (string.IsNullOrWhiteSpace(dllPath) || !File.Exists(dllPath))
                throw new FileNotFoundException("DLL not found.", dllPath);

            var plugins = GetPluginsDir(gameRoot);
            Directory.CreateDirectory(plugins);

            var fileName = Path.GetFileName(dllPath);
            var dest = Path.Combine(plugins, fileName);
            File.Copy(dllPath, dest, overwrite: true);
            
            string? primary = null;
            try
            {
                var scan = VersionScanner.ScanDll(dest);
                if (scan != null && !string.IsNullOrWhiteSpace(scan.Guid))
                    primary = dest;
            }
            catch { }

            return new InstallResult
            {
                TargetDir = plugins,
                PrimaryDll = primary ?? ""
            };
        }
        
        public static InstallResult InstallFromAny(string gameRoot, string path)
        {
            if (path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                return InstallDll(gameRoot, path);

            if (IsArchive(path))
                return InstallFromArchive(gameRoot, path);

            throw new InvalidOperationException("Unsupported file type for install: " + Path.GetFileName(path));
        }
        
        public static void TryDeleteDirectory(string dir)
        {
            if (!Directory.Exists(dir)) return;

            foreach (var file in Directory.GetFiles(dir, "*", SearchOption.TopDirectoryOnly))
            {
                try
                {
                    var fi = new FileInfo(file) { IsReadOnly = false };
                    fi.Attributes &= ~FileAttributes.ReadOnly;
                    File.Delete(file);
                }
                catch { /* ignore */ }
            }

            foreach (var sub in Directory.GetDirectories(dir, "*", SearchOption.TopDirectoryOnly))
            {
                TryDeleteDirectory(sub);
            }

            try
            {
                var di = new DirectoryInfo(dir) { Attributes = FileAttributes.Normal };
                Directory.Delete(dir, recursive: false);
            }
            catch
            {
                try { Directory.Delete(dir, recursive: true); } catch { }
            }
        }
        
        public static bool TryScanArchiveForPlugin(string archivePath, out VersionScanner.ModVersionInfo? info)
        {
            info = null;
            if (string.IsNullOrWhiteSpace(archivePath) || !File.Exists(archivePath)) return false;

            string tempDir = Path.Combine(Path.GetTempPath(), "ErenshorModInstaller_Peek_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDir);

            try
            {
                using var stream = File.OpenRead(archivePath);
                using var reader = ReaderFactory.Open(stream);
                while (reader.MoveToNextEntry())
                {
                    var entry = reader.Entry;
                    if (entry.IsDirectory) continue;

                    var name = Path.GetFileName(entry.Key ?? "");
                    if (string.IsNullOrEmpty(name)) continue;
                    if (!name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)) continue;

                    var outPath = Path.Combine(tempDir, name);
                    using (var outFs = File.Create(outPath))
                    {
                        reader.WriteEntryTo(outFs);
                    }

                    try
                    {
                        var scan = VersionScanner.ScanDll(outPath);
                        if (scan != null && !string.IsNullOrWhiteSpace(scan.Guid))
                        {
                            info = scan;
                            return true;
                        }
                    }
                    catch
                    {
                        // ignore malformed dll; keep scanning
                    }
                }

                return info != null;
            }
            catch
            {
                return false;
            }
            finally
            {
                try { Directory.Delete(tempDir, recursive: true); } catch { }
            }
        }

        private static bool IsArchive(string path)
        {
            var ext = Path.GetExtension(path)?.ToLowerInvariant();
            return ext == ".zip" || ext == ".7z" || ext == ".rar";
        }

        private static InstallResult InstallFromArchive(string gameRoot, string archivePath)
        {
            if (string.IsNullOrWhiteSpace(archivePath) || !File.Exists(archivePath))
                throw new FileNotFoundException("Archive not found.", archivePath);

            var plugins = GetPluginsDir(gameRoot);
            Directory.CreateDirectory(plugins);
            
            var extractedFiles = new List<string>();
            string? primaryDll = null;

            using (var stream = File.OpenRead(archivePath))
            using (var reader = ReaderFactory.Open(stream))
            {
                while (reader.MoveToNextEntry())
                {
                    var entry = reader.Entry;
                    if (entry.IsDirectory) continue;

                    var rawKey = entry.Key ?? "";
                    if (string.IsNullOrWhiteSpace(rawKey)) continue;
                    
                    var rel = rawKey.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
                    rel = rel.TrimStart(Path.DirectorySeparatorChar);
                    
                    var outPath = Path.Combine(plugins, rel);
                    var outDir = Path.GetDirectoryName(outPath)!;
                    Directory.CreateDirectory(outDir);

                    using (var outFs = File.Create(outPath))
                    {
                        reader.WriteEntryTo(outFs);
                    }

                    extractedFiles.Add(outPath);
                }
            }
            
            foreach (var f in extractedFiles.Where(f => f.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)))
            {
                try
                {
                    var scan = VersionScanner.ScanDll(f);
                    if (scan != null && !string.IsNullOrWhiteSpace(scan.Guid))
                    {
                        primaryDll = f;
                        break;
                    }
                }
                catch { /* ignore */ }
            }
            
            var topFolder = CommonTopLevelFolderUnder(plugins, extractedFiles);
            var targetDir = topFolder ?? plugins;

            return new InstallResult
            {
                TargetDir = targetDir,
                PrimaryDll = primaryDll ?? ""
            };
        }

        private static string? FindPrimaryPluginDll(string rootDir)
        {
            try
            {
                var dlls = Directory.GetFiles(rootDir, "*.dll", SearchOption.AllDirectories);
                foreach (var dll in dlls)
                {
                    var scan = VersionScanner.ScanDll(dll);
                    if (scan != null && !string.IsNullOrWhiteSpace(scan.Guid))
                        return dll;
                }
            }
            catch { }
            return null;
        }

        private static string? CommonTopLevelFolderUnder(string pluginsRoot, List<string> extractedFiles)
        {
            try
            {
                var tops = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var f in extractedFiles)
                {
                    var rel = Path.GetRelativePath(pluginsRoot, f);
                    var seg = rel.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries);
                    if (seg.Length > 1)
                        tops.Add(Path.Combine(pluginsRoot, seg[0]));
                    else
                        tops.Add(pluginsRoot); 
                }

                if (tops.Count == 1)
                {
                    var only = tops.First();
                    return string.Equals(only, pluginsRoot, StringComparison.OrdinalIgnoreCase) ? null : only;
                }
            }
            catch { }
            return null;
        }

        private static void CopyDirectory(string sourceDir, string destDir)
        {
            Directory.CreateDirectory(destDir);

            foreach (var file in Directory.GetFiles(sourceDir, "*", SearchOption.TopDirectoryOnly))
            {
                var name = Path.GetFileName(file);
                var destFile = Path.Combine(destDir, name);
                var fi = new FileInfo(file) { IsReadOnly = false };
                File.Copy(file, destFile, overwrite: true);
            }

            foreach (var sub in Directory.GetDirectories(sourceDir, "*", SearchOption.TopDirectoryOnly))
            {
                var name = Path.GetFileName(sub);
                var destSub = Path.Combine(destDir, name);
                CopyDirectory(sub, destSub);
            }
        }

        private static (bool exists, bool isTrue) TryReadHideManagerGameObject(string cfgPath)
        {
            try
            {
                var lines = File.ReadAllLines(cfgPath);
                var keys = new[] { "hidemanagergameobject", "hidemanagergameobjects" };

                foreach (var line in lines)
                {
                    var trimmed = line.Trim();
                    if (trimmed.StartsWith("#") || trimmed.StartsWith(";")) continue;
                    var eq = trimmed.IndexOf('=');
                    if (eq <= 0) continue;

                    var key = trimmed[..eq].Trim().ToLowerInvariant();
                    var val = trimmed[(eq + 1)..].Trim();

                    if (keys.Contains(key))
                    {
                        var isTrue = IsTruthy(val);
                        return (true, isTrue);
                    }
                }

                return (false, false);
            }
            catch
            {
                return (false, false);
            }
        }

        private static bool IsTruthy(string val)
        {
            if (string.IsNullOrWhiteSpace(val)) return false;
            var t = val.Trim().Trim('"').Trim().ToLowerInvariant();
            return t == "true" || t == "1" || t == "yes";
        }

        private static string SetHideManagerKeyToTrue(string text)
        {
            var lines = text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
            var keys = new[] { "hidemanagergameobject", "hidemanagergameobjects" };
            var changed = false;

            for (int i = 0; i < lines.Length; i++)
            {
                var src = lines[i];
                var trimmed = src.Trim();
                if (trimmed.StartsWith("#") || trimmed.StartsWith(";")) continue;

                var eq = trimmed.IndexOf('=');
                if (eq <= 0) continue;

                var key = trimmed[..eq].Trim();
                var val = trimmed[(eq + 1)..].Trim();

                if (keys.Contains(key.ToLowerInvariant()))
                {
                    var prefix = src[..src.IndexOf('=')];
                    lines[i] = $"{prefix}= true";
                    changed = true;
                    break;
                }
            }

            return changed ? string.Join(Environment.NewLine, lines) : text;
        }

        private static string AppendHideManagerKey(string text)
        {
            var sb = new StringBuilder();
            sb.AppendLine(text.TrimEnd());
            sb.AppendLine();
            sb.AppendLine("# Added by Erenshor Mod Installer");
            sb.AppendLine("HideManagerGameObject = true");
            return sb.ToString();
        }
    }
}
