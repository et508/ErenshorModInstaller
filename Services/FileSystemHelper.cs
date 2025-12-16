using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ErenshorModInstaller.Wpf.Services
{
    internal static class FileSystemHelper
    {
        public static void DeleteDirectory(string path)
        {
            if (!Directory.Exists(path)) return;

            RemoveReadOnlyAttributes(path);

            try
            {
                Directory.Delete(path, recursive: true);
            }
            catch
            {
                TryMoveToOldFolder(path);
            }
        }

        public static void CopyDirectory(string sourceDir, string destDir)
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

        public static void MoveDirectory(string sourceDir, string destDir)
        {
            if (!Directory.Exists(sourceDir))
                throw new DirectoryNotFoundException("Source folder not found: " + sourceDir);

            DeleteDirectory(destDir);
            Directory.CreateDirectory(Path.GetDirectoryName(destDir)!);

            try
            {
                Directory.Move(sourceDir, destDir);
            }
            catch
            {
                CopyDirectory(sourceDir, destDir);
                DeleteDirectory(sourceDir);
            }
        }

        public static void HideDirectory(string path)
        {
            if (!Directory.Exists(path)) return;
            try
            {
                var di = new DirectoryInfo(path);
                di.Attributes |= FileAttributes.Hidden;
            }
            catch { }
        }

        public static void CleanupIfEmpty(string dir)
        {
            try
            {
                if (!Directory.Exists(dir)) return;
                if (!Directory.EnumerateFileSystemEntries(dir).Any())
                    Directory.Delete(dir, false);
            }
            catch { }
        }

        public static bool IsArchive(string path)
        {
            var ext = Path.GetExtension(path)?.ToLowerInvariant();
            return ext == ".zip" || ext == ".7z" || ext == ".rar";
        }

        public static bool IsHiddenOrDotFolder(string path)
        {
            var name = Path.GetFileName(path);
            if (name?.StartsWith(".") == true) return true;

            try
            {
                var attr = File.GetAttributes(path);
                return (attr & FileAttributes.Hidden) != 0;
            }
            catch
            {
                return false;
            }
        }

        private static void RemoveReadOnlyAttributes(string path)
        {
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
        }

        private static void TryMoveToOldFolder(string path)
        {
            try
            {
                var tmp = path + ".old_" + DateTime.UtcNow.Ticks;
                Directory.Move(path, tmp);
            }
            catch { }
        }
    }
}
