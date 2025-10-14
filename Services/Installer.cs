using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace ErenshorModInstaller.Wpf.Services
{
    public static class Installer
    {
        public static string GetBepInExDir(string gameRoot) => Path.Combine(gameRoot, "BepInEx");
        public static string GetPluginsDir(string gameRoot) => Path.Combine(GetBepInExDir(gameRoot), "plugins");

        /// <summary>Throws if BepInEx 5.x doesn't look installed.</summary>
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

        /// <summary>
        /// Installs a zip by copying files from a detected content root (zip root or wrapper folder)
        /// into BepInEx/plugins/&lt;ZipName&gt;/...
        /// - No manifest required.
        /// - Recursively copies files from the detected content root.
        /// </summary>
        public static InstallResult InstallZip(string gameRoot, string zipPath)
        {
            if (!File.Exists(zipPath)) throw new FileNotFoundException("Zip not found.", zipPath);

            var plugins = GetPluginsDir(gameRoot);
            Directory.CreateDirectory(plugins);

            // Folder name derived from zip file name
            var folderName = SanitizeFolderName(Path.GetFileNameWithoutExtension(zipPath));
            if (string.IsNullOrWhiteSpace(folderName)) folderName = "Mod";

            // Extract to temp
            var temp = Path.Combine(Path.GetTempPath(), "er_mod_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temp);
            ZipFile.ExtractToDirectory(zipPath, temp, overwriteFiles: true);

            try
            {
                // Detect the "content root" inside the extracted tree
                var (contentRoot, warning) = DetectContentRoot(temp);

                var targetDir = Path.Combine(plugins, folderName);
                if (Directory.Exists(targetDir)) TryDeleteDirectory(targetDir);
                Directory.CreateDirectory(targetDir);

                // Copy everything recursively from contentRoot -> targetDir
                CopyAll(contentRoot, targetDir);

                // Ensure at least one DLL exists somewhere under target (warn if not)
                var hasAnyDll = Directory.GetFiles(targetDir, "*.dll", SearchOption.AllDirectories).Any();
                if (!hasAnyDll)
                {
                    warning = string.IsNullOrEmpty(warning)
                        ? "Warning: No DLL found in the installed files."
                        : warning + " Also: no DLL found in the installed files.";
                }

                return new InstallResult { TargetDir = targetDir, Warning = warning };
            }
            finally
            {
                TryDeleteDirectory(temp);
            }
        }

        /// <summary>
        /// Heuristics to find where the mod actually lives in the extracted zip:
        /// 1) If zip ROOT has any *.dll -> use ROOT.
        /// 2) If ROOT has exactly one directory and no files -> use that directory.
        /// 3) Else find the nearest directory that directly contains a *.dll (shallowest depth).
        /// 4) Fallback to ROOT.
        /// Returns (contentRootPath, optionalWarning).
        /// </summary>
        private static (string contentRoot, string? warning) DetectContentRoot(string extractedRoot)
        {
            // Case 1: DLLs at root
            var dllsAtRoot = Directory.GetFiles(extractedRoot, "*.dll", SearchOption.TopDirectoryOnly);
            if (dllsAtRoot.Length > 0)
                return (extractedRoot, null);

            // Case 2: Single top-level folder (common wrapper) and no files at root
            var filesAtRoot = Directory.GetFiles(extractedRoot, "*", SearchOption.TopDirectoryOnly);
            var dirsAtRoot = Directory.GetDirectories(extractedRoot, "*", SearchOption.TopDirectoryOnly);
            if (filesAtRoot.Length == 0 && dirsAtRoot.Length == 1)
            {
                var single = dirsAtRoot[0];
                return (single, "Detected a wrapper folder in the zip; installed its contents.");
            }

            // Case 3: Find shallowest directory that directly contains a DLL
            var shallowDllDir = Directory.GetDirectories(extractedRoot, "*", SearchOption.AllDirectories)
                .OrderBy(p => p.Count(c => c == Path.DirectorySeparatorChar || c == Path.AltDirectorySeparatorChar))
                .FirstOrDefault(dir => Directory.GetFiles(dir, "*.dll", SearchOption.TopDirectoryOnly).Length > 0);

            if (!string.IsNullOrEmpty(shallowDllDir))
            {
                return (shallowDllDir, "Installed from the subfolder that contains the mod DLLs.");
            }

            // Case 4: Fallback
            return (extractedRoot, "Could not find DLLs; installed from zip root. If files were nested, they might be too deep.");
        }

        private static void CopyAll(string src, string dst)
        {
            // Create directories
            foreach (var dirPath in Directory.GetDirectories(src, "*", SearchOption.AllDirectories))
            {
                var rel = Path.GetRelativePath(src, dirPath);
                Directory.CreateDirectory(Path.Combine(dst, rel));
            }
            // Copy files
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
