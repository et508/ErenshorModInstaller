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
    /// <summary>
    /// File-system level install/validation helpers.
    /// - No UI here (no MessageBox). Pure operations + exceptions & return types.
    /// - Archives handled via SharpCompress (.zip, .7z, .rar).
    /// - Placement rules:
    ///   * Dropped folder => copy the whole folder to BepInEx/plugins/<FolderName>
    ///   * Archive => extract preserving relative paths under BepInEx/plugins (so "coolmod/..." -> plugins/coolmod/..., root DLLs -> plugins/*.dll)
    ///   * Bare DLL => copy into BepInEx/plugins
    /// </summary>
    public static class Installer
    {
        // ---------------------------- Public API ----------------------------

        public sealed class InstallResult
        {
            public string TargetDir { get; set; } = ""; // Where the content landed (plugins or plugins/<folder>)
            public string PrimaryDll { get; set; } = ""; // Best-guess plugin DLL that contains BepInPlugin, else empty
            public string Warning { get; set; } = "";    // Any non-fatal warning to surface
        }

        public enum BepInExConfigStatus
        {
            Ok,
            MissingConfig,
            MissingKey,
            WrongValue
        }

        /// <summary>
        /// Throws with a user-friendly message if BepInEx is not present correctly.
        /// Returns a version string if detected (best-effort).
        /// </summary>
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

            // Try to read file version from BepInEx.dll (best-effort)
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

        /// <summary>
        /// Reads BepInEx.cfg and ensures the HideManagerGameObject setting is present/true.
        /// We only report status here — no file creation. Caller decides whether to offer fixing.
        /// </summary>
        public static BepInExConfigStatus GetBepInExConfigStatus(string gameRoot, out string cfgPath)
        {
            cfgPath = Path.Combine(GetBepInExDir(gameRoot), "config", "BepInEx.cfg");
            if (!File.Exists(cfgPath)) return BepInExConfigStatus.MissingConfig;

            // We accept either "HideManagerGameObject" or "HideManagerGameObjects" (typos happen).
            // True values: true, 1, yes (case-insensitive).
            var (exists, isTrue) = TryReadHideManagerGameObject(cfgPath);
            if (!exists) return BepInExConfigStatus.MissingKey;
            if (!isTrue) return BepInExConfigStatus.WrongValue;
            return BepInExConfigStatus.Ok;
        }

        /// <summary>
        /// Updates BepInEx.cfg (must already exist) to set HideManagerGameObject(s)=true.
        /// Throws on errors. Does not create the file.
        /// </summary>
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
                // Key not found, append into a suitable section (best-effort)
                var appended = AppendHideManagerKey(text);
                File.WriteAllText(cfgPath, appended, new UTF8Encoding(false));
            }
        }

        /// <summary>
        /// Install a whole folder: copy to plugins/&lt;folderName&gt; preserving structure.
        /// </summary>
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

        /// <summary>
        /// Install a single DLL to plugins root.
        /// </summary>
        public static InstallResult InstallDll(string gameRoot, string dllPath)
        {
            if (string.IsNullOrWhiteSpace(dllPath) || !File.Exists(dllPath))
                throw new FileNotFoundException("DLL not found.", dllPath);

            var plugins = GetPluginsDir(gameRoot);
            Directory.CreateDirectory(plugins);

            var fileName = Path.GetFileName(dllPath);
            var dest = Path.Combine(plugins, fileName);
            File.Copy(dllPath, dest, overwrite: true);

            // Primary dll is the file we just copied (if it’s a plugin)
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

        /// <summary>
        /// Install from any file: .zip/.7z/.rar => extract; .dll => InstallDll; otherwise throws.
        /// Extraction preserves archive relative paths under plugins.
        /// </summary>
        public static InstallResult InstallFromAny(string gameRoot, string path)
        {
            if (path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                return InstallDll(gameRoot, path);

            if (IsArchive(path))
                return InstallFromArchive(gameRoot, path);

            throw new InvalidOperationException("Unsupported file type for install: " + Path.GetFileName(path));
        }

        /// <summary>
        /// Robust directory deletion (read-only, nested).
        /// </summary>
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
                // fallback
                try { Directory.Delete(dir, recursive: true); } catch { }
            }
        }

        /// <summary>
        /// Peek inside an archive and extract only DLLs to a temp directory to find a plugin DLL.
        /// Returns true and sets info if found.
        /// </summary>
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

        // ---------------------------- Internals ----------------------------

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

            // Extract preserving relative paths under plugins
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

                    // Normalize entry path to OS separators
                    var rel = rawKey.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
                    rel = rel.TrimStart(Path.DirectorySeparatorChar);

                    // Where to place inside plugins
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

            // Best-guess PrimaryDll: first extracted DLL that has BepInPlugin
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

            // Resolve TargetDir heuristically:
            // If all extracted files share a common top-level folder under plugins, return that folder; else plugins.
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
                        tops.Add(pluginsRoot); // file at root => no single top folder
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
            // We accept either key name: HideManagerGameObject or HideManagerGameObjects
            // Values considered TRUE: "true", "1", "yes" (case-insensitive, trimmed).
            // Very forgiving INI parsing: scan all non-comment lines "key = value".
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
            // Replace value for first occurrence of either key; keep original casing/spacing style loosely
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
                    // Preserve key casing, normalize to "true"
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
            // Append to end within a [BepInEx] or general section if one exists; otherwise add a small section.
            var sb = new StringBuilder();
            sb.AppendLine(text.TrimEnd());
            sb.AppendLine();
            sb.AppendLine("# Added by Erenshor Mod Installer");
            sb.AppendLine("HideManagerGameObject = true");
            return sb.ToString();
        }
    }
}
