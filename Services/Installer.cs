using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using SharpCompress.Archives;
using SharpCompress.Common;

namespace ErenshorModInstaller.Wpf.Services
{
    public static class Installer
    {
        public static string GetBepInExDir(string gameRoot) => Path.Combine(gameRoot, "BepInEx");
        public static string GetPluginsDir(string gameRoot) => Path.Combine(GetBepInExDir(gameRoot), "plugins");
        public static string GetConfigDir(string gameRoot)  => Path.Combine(GetBepInExDir(gameRoot), "config");
        public static string GetBepInExCfgPath(string gameRoot) => Path.Combine(GetConfigDir(gameRoot), "BepInEx.cfg");
        
        public static string? ValidateBepInExOrThrow(string gameRoot)
        {
            var bepin   = GetBepInExDir(gameRoot);
            var core    = Path.Combine(bepin, "core", "BepInEx.dll");
            var winhttp = Path.Combine(gameRoot, "winhttp.dll");
            var doorstop= Path.Combine(gameRoot, "doorstop_config.ini");

            if (!Directory.Exists(bepin))
                throw new InvalidOperationException("BepInEx folder not found. Please install BepInEx 5.x (x64) into the Erenshor folder.");

            if (!File.Exists(core) || !File.Exists(winhttp) || !File.Exists(doorstop))
                throw new InvalidOperationException(
                    "BepInEx not fully detected. Install BepInEx 5.x (x64) into the Erenshor folder, then run the game once.");

            try { return FileVersionInfo.GetVersionInfo(core).FileVersion; }
            catch { return null; }
        }

        public enum BepInExConfigStatus
        {
            Ok,
            MissingConfig,
            MissingKey,
            WrongValue
        }
        
        public static BepInExConfigStatus GetBepInExConfigStatus(string gameRoot, out string cfgPath)
        {
            cfgPath = GetBepInExCfgPath(gameRoot);

            if (!File.Exists(cfgPath))
                return BepInExConfigStatus.MissingConfig;

            try
            {
                var lines = File.ReadAllLines(cfgPath);
                var (idx, keyFound, isTrue) = FindHideManagerLine(lines);

                if (!keyFound) return BepInExConfigStatus.MissingKey;
                return isTrue ? BepInExConfigStatus.Ok : BepInExConfigStatus.WrongValue;
            }
            catch
            {
                return BepInExConfigStatus.MissingKey;
            }
        }
        
        public static void EnsureHideManagerGameObjectTrue(string gameRoot)
        {
            var cfgPath = GetBepInExCfgPath(gameRoot);
            if (!File.Exists(cfgPath))
                throw new InvalidOperationException(
                    "BepInEx.cfg is missing. Please run Erenshor once after installing BepInEx to generate it.");

            var lines = File.ReadAllLines(cfgPath);
            var bak = cfgPath + ".bak";
            try { File.Copy(cfgPath, bak, overwrite: true); } catch { /* best effort */ }

            var (idx, keyFound, _) = FindHideManagerLine(lines);

            if (keyFound)
            {
                lines[idx] = SetKeyLineToTrue(lines[idx]);
            }
            else
            {
                int bepIdx = FindSectionIndex(lines, "BepInEx");
                var insertLine = "HideManagerGameObject = true";

                if (bepIdx >= 0)
                {
                    int insertAt = bepIdx + 1;
                    while (insertAt < lines.Length && IsBlankOrComment(lines[insertAt])) insertAt++;
                    lines = InsertLine(lines, insertAt, insertLine);
                }
                else
                {
                    lines = InsertLine(lines, lines.Length, "");
                    lines = InsertLine(lines, lines.Length, "[BepInEx]");
                    lines = InsertLine(lines, lines.Length, insertLine);
                }
            }

            File.WriteAllLines(cfgPath, lines);
        }
        
        private static (int index, bool keyFound, bool isTrue) FindHideManagerLine(string[] lines)
        {
            var keys = new[] { "hidemanagergameobject", "hidemanagergameobjects" };
            for (int i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                if (IsBlankOrComment(line)) continue;

                var eq = line.IndexOf('=');
                if (eq <= 0) continue;

                var key = line.Substring(0, eq).Trim().ToLowerInvariant();
                if (!keys.Contains(key)) continue;

                var val = line.Substring(eq + 1).Trim();
                var isTrue = val.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                             val.Equals("1", StringComparison.OrdinalIgnoreCase) ||
                             val.Equals("yes", StringComparison.OrdinalIgnoreCase);

                return (i, true, isTrue);
            }
            return (-1, false, false);
        }

        private static bool IsBlankOrComment(string line)
        {
            if (string.IsNullOrWhiteSpace(line)) return true;
            var t = line.TrimStart();
            return t.StartsWith("#") || t.StartsWith(";") || t.StartsWith("//");
        }

        private static string SetKeyLineToTrue(string line)
        {
            var eq = line.IndexOf('=');
            if (eq <= 0) return "HideManagerGameObject = true";
            var left = line.Substring(0, eq).Trim();
            return left + " = true";
        }

        private static int FindSectionIndex(string[] lines, string sectionNameNoBrackets)
        {
            var want = "[" + sectionNameNoBrackets.Trim() + "]";
            for (int i = 0; i < lines.Length; i++)
                if (string.Equals(lines[i].Trim(), want, StringComparison.OrdinalIgnoreCase))
                    return i;
            return -1;
        }

        private static string[] InsertLine(string[] lines, int index, string newLine)
        {
            if (index < 0) index = 0;
            if (index > lines.Length) index = lines.Length;
            var arr = new string[lines.Length + 1];
            Array.Copy(lines, 0, arr, 0, index);
            arr[index] = newLine;
            Array.Copy(lines, index, arr, index + 1, lines.Length - index);
            return arr;
        }
        
        public static InstallResult InstallFromAny(string gameRoot, string archivePath)
        {
            var ext = Path.GetExtension(archivePath).ToLowerInvariant();
            return ext switch
            {
                ".zip" => InstallZip(gameRoot, archivePath),
                ".7z" or ".rar" => InstallArchiveSharpCompress(gameRoot, archivePath),
                _ => throw new NotSupportedException("Unsupported file type: " + ext)
            };
        }
        
        public static InstallResult InstallFromDirectory(string gameRoot, string folderPath)
        {
            if (!Directory.Exists(folderPath)) throw new DirectoryNotFoundException(folderPath);
            
            var bepin   = GetBepInExDir(gameRoot);
            var plugins = GetPluginsDir(gameRoot);
            if (!Directory.Exists(bepin))
                throw new InvalidOperationException("BepInEx not found. Please install BepInEx 5.4.x into the game folder.");

            var folderName = SanitizeFolderName(new DirectoryInfo(folderPath).Name);
            var targetDir = Path.Combine(plugins, folderName);
            if (Directory.Exists(targetDir)) TryDeleteDirectory(targetDir);
            Directory.CreateDirectory(targetDir); // allowed (inside existing plugins)

            var (contentRoot, warning) = DetectContentRoot(folderPath);
            CopyAll(contentRoot, targetDir);

            var hasDll = Directory.GetFiles(targetDir, "*.dll", SearchOption.AllDirectories).Any();
            if (!hasDll)
                warning = string.IsNullOrEmpty(warning) ? "Warning: No DLL found in the installed files." : warning + " Also: no DLL found.";

            return new InstallResult { TargetDir = targetDir, Warning = warning };
        }
        
        public static InstallResult InstallZip(string gameRoot, string zipPath)
        {
            var temp = ExtractZipToTemp(zipPath);
            try { return InstallFromExtracted(gameRoot, zipPath, temp); }
            finally { TryDeleteDirectory(temp); }
        }

        private static string ExtractZipToTemp(string zipPath)
        {
            if (!File.Exists(zipPath)) throw new FileNotFoundException("Zip not found.", zipPath);
            var temp = Path.Combine(Path.GetTempPath(), "er_mod_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temp);
            ZipFile.ExtractToDirectory(zipPath, temp, overwriteFiles: true);
            return temp;
        }
        
        public static InstallResult InstallArchiveSharpCompress(string gameRoot, string archivePath)
        {
            if (!File.Exists(archivePath)) throw new FileNotFoundException("Archive not found.", archivePath);

            var temp = Path.Combine(Path.GetTempPath(), "er_mod_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temp);

            using (var archive = ArchiveFactory.Open(archivePath))
            {
                foreach (var entry in archive.Entries.Where(e => !e.IsDirectory))
                {
                    var outPath = Path.Combine(temp, entry.Key.Replace('/', Path.DirectorySeparatorChar));
                    Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
                    entry.WriteToFile(outPath, new ExtractionOptions
                    {
                        ExtractFullPath = true,
                        Overwrite = true
                    });
                }
            }

            try { return InstallFromExtracted(gameRoot, archivePath, temp); }
            finally { TryDeleteDirectory(temp); }
        }
        
        private static InstallResult InstallFromExtracted(string gameRoot, string sourcePath, string extractedRoot)
        {
            var bepin   = GetBepInExDir(gameRoot);
            var plugins = GetPluginsDir(gameRoot);
            if (!Directory.Exists(bepin))
                throw new InvalidOperationException("BepInEx not found. Please install BepInEx 5.4.x into the game folder.");

            var folderName = SanitizeFolderName(Path.GetFileNameWithoutExtension(sourcePath));
            if (string.IsNullOrWhiteSpace(folderName)) folderName = "Mod";

            var (contentRoot, warning) = DetectContentRoot(extractedRoot);

            var targetDir = Path.Combine(plugins, folderName);
            if (Directory.Exists(targetDir)) TryDeleteDirectory(targetDir);
            Directory.CreateDirectory(targetDir); 

            CopyAll(contentRoot, targetDir);

            var hasAnyDll = Directory.GetFiles(targetDir, "*.dll", SearchOption.AllDirectories).Any();
            if (!hasAnyDll)
                warning = string.IsNullOrEmpty(warning) ? "Warning: No DLL found in the installed files." : warning + " Also: no DLL found.";

            return new InstallResult { TargetDir = targetDir, Warning = warning };
        }
        
        public static (string contentRoot, string? warning) DetectContentRoot(string extractedRoot)
        {
            var dllsAtRoot = Directory.GetFiles(extractedRoot, "*.dll", SearchOption.TopDirectoryOnly);
            if (dllsAtRoot.Length > 0) return (extractedRoot, null);

            var filesAtRoot = Directory.GetFiles(extractedRoot, "*", SearchOption.TopDirectoryOnly);
            var dirsAtRoot  = Directory.GetDirectories(extractedRoot, "*", SearchOption.TopDirectoryOnly);
            if (filesAtRoot.Length == 0 && dirsAtRoot.Length == 1)
                return (dirsAtRoot[0], "Detected a wrapper folder; installed its contents.");

            var shallowDllDir = Directory.GetDirectories(extractedRoot, "*", SearchOption.AllDirectories)
                .OrderBy(p => p.Count(c => c == Path.DirectorySeparatorChar || c == Path.AltDirectorySeparatorChar))
                .FirstOrDefault(dir => Directory.GetFiles(dir, "*.dll", SearchOption.TopDirectoryOnly).Any());

            if (!string.IsNullOrEmpty(shallowDllDir))
                return (shallowDllDir, "Installed from the subfolder that contains the mod DLLs.");

            return (extractedRoot, "Could not find DLLs; installed from root. If files were nested, they may be too deep.");
        }

        private static void CopyAll(string src, string dst)
        {
            foreach (var dirPath in Directory.GetDirectories(src, "*", SearchOption.AllDirectories))
            {
                var rel = Path.GetRelativePath(src, dirPath);
                Directory.CreateDirectory(Path.Combine(dst, rel));
            }
            foreach (var filePath in Directory.GetFiles(src, "*", SearchOption.AllDirectories))
            {
                var rel = Path.GetRelativePath(src, filePath);
                var to  = Path.Combine(dst, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(to)!);
                File.Copy(filePath, to, overwrite: true);
            }
        }

        public static void TryDeleteDirectory(string dir)
        {
            if (!Directory.Exists(dir)) return;
            foreach (var f in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
            {
                try { new FileInfo(f) { IsReadOnly = false }.Refresh(); } catch { }
            }
            Directory.Delete(dir, recursive: true);
        }

        private static string SanitizeFolderName(string name)
        {
            foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            name = name.Trim();
            if (name.Length > 64) name = name[..64];
            return string.IsNullOrWhiteSpace(name) ? "Mod" : name;
        }

        public sealed class InstallResult
        {
            public string TargetDir { get; set; } = "";
            public string? Warning { get; set; }
        }
    }
}
