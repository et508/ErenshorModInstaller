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

        public static string? ValidateBepInExOrThrow(string gameRoot)
        {
            var bepin = GetBepInExDir(gameRoot);
            var core = Path.Combine(bepin, "core", "BepInEx.dll");
            var winhttp = Path.Combine(gameRoot, "winhttp.dll");
            var doorstop = Path.Combine(gameRoot, "doorstop_config.ini");

            if (!File.Exists(core) || !File.Exists(winhttp) || !File.Exists(doorstop))
                throw new InvalidOperationException(
                    "BepInEx not detected.\nInstall BepInEx 5.x (x64) into the Erenshor folder, run the game once, then try again.");

            try { return FileVersionInfo.GetVersionInfo(core).FileVersion; }
            catch { return null; }
        }

        // === PUBLIC INSTALLERS ===

        public static InstallResult InstallFromAny(string gameRoot, string archivePath)
        {
            var ext = Path.GetExtension(archivePath).ToLowerInvariant();
            if (ext == ".zip") return InstallZip(gameRoot, archivePath);
            if (ext == ".7z" || ext == ".rar") return InstallArchiveSharpCompress(gameRoot, archivePath);
            throw new NotSupportedException("Unsupported file type: " + ext);
        }

        public static InstallResult InstallFromDirectory(string gameRoot, string folderPath)
        {
            if (!Directory.Exists(folderPath)) throw new DirectoryNotFoundException(folderPath);

            var plugins = GetPluginsDir(gameRoot);
            Directory.CreateDirectory(plugins);

            var folderName = SanitizeFolderName(new DirectoryInfo(folderPath).Name);
            var targetDir = Path.Combine(plugins, folderName);
            if (Directory.Exists(targetDir)) TryDeleteDirectory(targetDir);
            Directory.CreateDirectory(targetDir);

            // Detect content root inside the provided folder (same heuristic as archives)
            var (contentRoot, warning) = DetectContentRoot(folderPath);

            CopyAll(contentRoot, targetDir);

            var hasDll = Directory.GetFiles(targetDir, "*.dll", SearchOption.AllDirectories).Any();
            if (!hasDll)
                warning = string.IsNullOrEmpty(warning) ? "Warning: No DLL found in the installed files." : warning + " Also: no DLL found.";

            return new InstallResult { TargetDir = targetDir, Warning = warning };
        }

        // === ZIP (built-in) ===
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

        // === 7z/RAR (SharpCompress) ===
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
                    entry.WriteToFile(outPath, new ExtractionOptions { ExtractFullPath = true, Overwrite = true });
                }
            }

            try { return InstallFromExtracted(gameRoot, archivePath, temp); }
            finally { TryDeleteDirectory(temp); }
        }

        // === Shared extracted install ===
        private static InstallResult InstallFromExtracted(string gameRoot, string sourcePath, string extractedRoot)
        {
            var plugins = GetPluginsDir(gameRoot);
            Directory.CreateDirectory(plugins);

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

        // === Heuristics & helpers ===

        private static (string contentRoot, string? warning) DetectContentRoot(string extractedRoot)
        {
            var dllsAtRoot = Directory.GetFiles(extractedRoot, "*.dll", SearchOption.TopDirectoryOnly);
            if (dllsAtRoot.Length > 0) return (extractedRoot, null);

            var filesAtRoot = Directory.GetFiles(extractedRoot, "*", SearchOption.TopDirectoryOnly);
            var dirsAtRoot = Directory.GetDirectories(extractedRoot, "*", SearchOption.TopDirectoryOnly);
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
                var to = Path.Combine(dst, rel);
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
