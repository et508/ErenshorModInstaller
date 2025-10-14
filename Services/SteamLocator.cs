using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace ErenshorModInstaller.Wpf.Services
{
    public static class SteamLocator
    {
        public static string? TryFindErenshorRoot()
        {
            foreach (var lib in GetSteamLibraries())
            {
                try
                {
                    var common = Path.Combine(lib, "steamapps", "common");
                    if (!Directory.Exists(common)) continue;

                    var er = Path.Combine(common, "Erenshor");
                    if (Directory.Exists(er)) return er;

                    foreach (var dir in Directory.GetDirectories(common))
                        if (string.Equals(Path.GetFileName(dir), "Erenshor", StringComparison.OrdinalIgnoreCase))
                            return dir;
                }
                catch { }
            }
            return null;
        }

        private static IEnumerable<string> GetSteamLibraries()
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var regPaths = new[]
            {
                @"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam",
                @"HKEY_CURRENT_USER\SOFTWARE\Valve\Steam"
            };
            foreach (var reg in regPaths)
            {
                var path = Registry.GetValue(reg, "InstallPath", null) as string
                           ?? Registry.GetValue(reg, "SteamPath", null) as string;
                if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
                {
                    set.Add(path);
                    foreach (var lib in ParseLibraryFolders(path)) set.Add(lib);
                }
            }

            var defaultPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam");
            if (Directory.Exists(defaultPath))
            {
                set.Add(defaultPath);
                foreach (var lib in ParseLibraryFolders(defaultPath)) set.Add(lib);
            }

            return set;
        }

        private static IEnumerable<string> ParseLibraryFolders(string steamRoot)
        {
            var list = new List<string>();
            try
            {
                var vdf = Path.Combine(steamRoot, "steamapps", "libraryfolders.vdf");
                if (!File.Exists(vdf)) return list;

                var text = File.ReadAllText(vdf);
                var rx = new Regex("\"path\"\\s*\"([^\"]+)\"", RegexOptions.IgnoreCase);
                foreach (Match m in rx.Matches(text))
                {
                    var p = m.Groups[1].Value.Replace("\\\\", "\\");
                    if (Directory.Exists(p)) list.Add(p);
                }
            }
            catch { }
            return list;
        }
    }
}
