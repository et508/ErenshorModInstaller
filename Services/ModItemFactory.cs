using System;
using System.IO;
using ErenshorModInstaller.Wpf.Services.Models;

namespace ErenshorModInstaller.Wpf.Services
{
    internal static class ModItemFactory
    {
        public static ModItem? CreateFromFolder(string folderPath)
        {
            VersionScanner.ModVersionInfo? scan = null;
            try { scan = VersionScanner.ScanFolder(folderPath); } catch { }
            
            if (scan == null || string.IsNullOrWhiteSpace(scan.Guid))
                return null;

            return new ModItem
            {
                Guid = scan.Guid,
                DisplayName = GetDisplayName(scan.Name, scan.Guid),
                Version = GetVersion(scan.Version),
                IsFolder = true,
                FolderName = Path.GetFileName(folderPath),
                PluginDllFullPath = scan.DllPath,
                IsEnabled = DllStateManager.IsEnabled(scan.DllPath)
            };
        }

        public static ModItem? CreateFromDll(string dllPath, string baseName)
        {
            VersionScanner.ModVersionInfo? scan = null;
            try { scan = VersionScanner.ScanDll(dllPath); } catch { }
            
            if (scan == null || string.IsNullOrWhiteSpace(scan.Guid))
                return null;

            return new ModItem
            {
                Guid = scan.Guid,
                DisplayName = GetDisplayName(scan.Name, scan.Guid),
                Version = GetVersion(scan.Version),
                IsFolder = false,
                DllFileName = baseName + ".dll",
                PluginDllFullPath = dllPath,
                IsEnabled = File.Exists(DllStateManager.GetEnabledPath(dllPath))
            };
        }

        private static string GetDisplayName(string? name, string guid)
        {
            return string.IsNullOrWhiteSpace(name) ? guid : name;
        }

        private static string GetVersion(string? version)
        {
            return string.IsNullOrWhiteSpace(version) ? "0.0.0" : version;
        }
    }
}
