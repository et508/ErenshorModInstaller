using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ErenshorModInstaller.Wpf.Services
{
    public static class ModIndex
    {
        public sealed class StoredMod
        {
            [JsonPropertyName("guid")]    public string Guid { get; set; } = "";
            [JsonPropertyName("name")]    public string Name { get; set; } = "";
            [JsonPropertyName("version")] public string Version { get; set; } = "0.0.0";
        }

        private static readonly JsonSerializerOptions JsonOpts = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };

        public static string GetIndexDir(string gameRoot)
        {
            var bep = Installer.GetBepInExDir(gameRoot);
            return Path.Combine(bep, "ErenshorModInstaller");
        }

        public static string GetIndexPath(string gameRoot) => Path.Combine(GetIndexDir(gameRoot), "ModIndex.json");

        public static Dictionary<string, StoredMod> Load(string gameRoot)
        {
            try
            {
                var path = GetIndexPath(gameRoot);
                if (!File.Exists(path)) return new(StringComparer.OrdinalIgnoreCase);
                var json = File.ReadAllText(path);
                var dict = JsonSerializer.Deserialize<Dictionary<string, StoredMod>>(json, JsonOpts);
                return dict ?? new(StringComparer.OrdinalIgnoreCase);
            }
            catch
            {
                return new(StringComparer.OrdinalIgnoreCase);
            }
        }

        public static void Save(string gameRoot, Dictionary<string, StoredMod> index)
        {
            var dir = GetIndexDir(gameRoot);
            Directory.CreateDirectory(dir);
            var path = GetIndexPath(gameRoot);
            var json = JsonSerializer.Serialize(index, JsonOpts);
            File.WriteAllText(path, json);
        }

        public static void UpsertFromScan(string gameRoot, VersionScanner.ModVersionInfo scan)
        {
            if (scan == null) return;
            if (string.IsNullOrWhiteSpace(scan.Guid)) return;

            var index = Load(gameRoot);
            index[scan.Guid] = new StoredMod
            {
                Guid = scan.Guid,
                Name = string.IsNullOrWhiteSpace(scan.Name) ? scan.Guid : scan.Name,
                Version = string.IsNullOrWhiteSpace(scan.Version) ? "0.0.0" : scan.Version
            };
            Save(gameRoot, index);
        }

        public static void RebuildFromExistingMods(string gameRoot)
        {
            var plugins = Installer.GetPluginsDir(gameRoot);
            if (!Directory.Exists(plugins)) return;

            var fresh = new Dictionary<string, StoredMod>(StringComparer.OrdinalIgnoreCase);

            // Folder mods
            foreach (var dir in Directory.GetDirectories(plugins))
            {
                try
                {
                    var scan = VersionScanner.ScanFolder(dir);
                    if (scan == null || string.IsNullOrWhiteSpace(scan.Guid)) continue;

                    fresh[scan.Guid] = new StoredMod
                    {
                        Guid = scan.Guid,
                        Name = string.IsNullOrWhiteSpace(scan.Name) ? scan.Guid : scan.Name,
                        Version = string.IsNullOrWhiteSpace(scan.Version) ? "0.0.0" : scan.Version
                    };
                }
                catch { }
            }

            // Top-level plugins (enabled & disabled)
            foreach (var f in Directory.GetFiles(plugins, "*.dll", SearchOption.TopDirectoryOnly))
            {
                try
                {
                    var scan = VersionScanner.ScanDll(f);
                    if (scan == null || string.IsNullOrWhiteSpace(scan.Guid)) continue;

                    fresh[scan.Guid] = new StoredMod
                    {
                        Guid = scan.Guid,
                        Name = string.IsNullOrWhiteSpace(scan.Name) ? scan.Guid : scan.Name,
                        Version = string.IsNullOrWhiteSpace(scan.Version) ? "0.0.0" : scan.Version
                    };
                }
                catch { }
            }
            foreach (var f in Directory.GetFiles(plugins, "*.dll.disabled", SearchOption.TopDirectoryOnly))
            {
                try
                {
                    var scan = VersionScanner.ScanDll(f);
                    if (scan == null || string.IsNullOrWhiteSpace(scan.Guid)) continue;

                    fresh[scan.Guid] = new StoredMod
                    {
                        Guid = scan.Guid,
                        Name = string.IsNullOrWhiteSpace(scan.Name) ? scan.Guid : scan.Name,
                        Version = string.IsNullOrWhiteSpace(scan.Version) ? "0.0.0" : scan.Version
                    };
                }
                catch { }
            }

            Save(gameRoot, fresh);
        }

        /// <summary>
        /// Ensures the index exists and has the minimal schema (guid/name/version).
        /// If missing, empty, or old-shaped (e.g., contains "confidence" / "primaryDll"), rebuild from plugins.
        /// </summary>
        public static void EnsureMinimalIndex(string gameRoot)
        {
            var path = GetIndexPath(gameRoot);
            if (!File.Exists(path))
            {
                RebuildFromExistingMods(gameRoot);
                return;
            }

            try
            {
                var json = File.ReadAllText(path);
                if (string.IsNullOrWhiteSpace(json))
                {
                    RebuildFromExistingMods(gameRoot);
                    return;
                }

                // crude old-shape detection to auto-migrate
                if (json.Contains("\"confidence\"", StringComparison.OrdinalIgnoreCase) ||
                    json.Contains("\"primaryDll\"", StringComparison.OrdinalIgnoreCase) ||
                    json.Contains("\"sha256\"", StringComparison.OrdinalIgnoreCase))
                {
                    RebuildFromExistingMods(gameRoot);
                    return;
                }
            }
            catch
            {
                RebuildFromExistingMods(gameRoot);
            }
        }
    }
}
