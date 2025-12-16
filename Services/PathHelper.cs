using System;
using System.IO;

namespace ErenshorModInstaller.Wpf.Services
{
    internal static class PathHelper
    {
        public static string GetBepInExDir(string gameRoot) => Path.Combine(gameRoot, "BepInEx");

        public static string GetPluginsDir(string gameRoot) => Path.Combine(GetBepInExDir(gameRoot), "plugins");

        public static string GetConfigDir(string gameRoot) => Path.Combine(GetBepInExDir(gameRoot), "config");

        public static string GetBepInExConfigPath(string gameRoot) => Path.Combine(GetConfigDir(gameRoot), "BepInEx.cfg");

        public static string GetVersionStoreRoot(string gameRoot) => Path.Combine(GetBepInExDir(gameRoot), ".versions");

        public static string GetVersionStoreForGuid(string gameRoot, string guid)
        {
            var safeGuid = SanitizeForPath(guid);
            return Path.Combine(GetVersionStoreRoot(gameRoot), safeGuid);
        }

        public static string RelativeToGame(string gameRoot, string fullPath)
        {
            return string.IsNullOrWhiteSpace(gameRoot) ? fullPath : fullPath.Replace(gameRoot, ".").Trim();
        }

        private static string SanitizeForPath(string input)
        {
            var invalid = Path.GetInvalidFileNameChars();
            foreach (var c in invalid)
            {
                input = input.Replace(c, '_');
            }
            return input;
        }
    }
}
