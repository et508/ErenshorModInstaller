using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace ErenshorModInstaller.Wpf.Services
{
    /// <summary>
    /// Manages the mod installation index in ModIndex.json
    /// Tracks installed mods, versions, and files
    /// </summary>
    public static class ModIndex
    {
        // JSON serialization options
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };

        private static string GetIndexPath(string gameRoot)
        {
            // Use centralized AppData location
            return AppDataManager.GetModIndexPath();
        }

        /// <summary>
        /// Loads the mod index from disk
        /// </summary>
        public static Dictionary<string, ModIndexEntry> Load(string gameRoot)
        {
            AppDataManager.EnsureAppDataFolder();
            
            var path = GetIndexPath(gameRoot);
            if (!File.Exists(path))
                return new Dictionary<string, ModIndexEntry>(StringComparer.OrdinalIgnoreCase);

            try
            {
                var json = File.ReadAllText(path);
                return JsonSerializer.Deserialize<Dictionary<string, ModIndexEntry>>(json, JsonOptions)
                       ?? new Dictionary<string, ModIndexEntry>(StringComparer.OrdinalIgnoreCase);
            }
            catch
            {
                return new Dictionary<string, ModIndexEntry>(StringComparer.OrdinalIgnoreCase);
            }
        }

        /// <summary>
        /// Saves the mod index to disk
        /// </summary>
        public static void Save(string gameRoot, Dictionary<string, ModIndexEntry> index)
        {
            AppDataManager.EnsureAppDataFolder();
            
            var path = GetIndexPath(gameRoot);
            try
            {
                var json = JsonSerializer.Serialize(index, JsonOptions);
                File.WriteAllText(path, json);
            }
            catch { }
        }

        /// <summary>
        /// Ensures a minimal index file exists
        /// </summary>
        public static void EnsureMinimalIndex(string gameRoot)
        {
            AppDataManager.EnsureAppDataFolder();
            
            var path = GetIndexPath(gameRoot);
            if (!File.Exists(path))
            {
                Save(gameRoot, new Dictionary<string, ModIndexEntry>(StringComparer.OrdinalIgnoreCase));
            }
        }

        /// <summary>
        /// Records a mod installation with full metadata
        /// </summary>
        public static void RecordInstall(string gameRoot, string guid, string friendlyName, string version, List<string> files, string? homepageUrl = null)
        {
            var index = Load(gameRoot);
            if (!index.ContainsKey(guid))
            {
                index[guid] = new ModIndexEntry
                {
                    Guid = guid,
                    FriendlyName = friendlyName,
                    Versions = new Dictionary<string, VersionRecord>(StringComparer.OrdinalIgnoreCase)
                };
            }

            var entry = index[guid];
            entry.FriendlyName = friendlyName;

            if (!string.IsNullOrEmpty(homepageUrl))
                entry.HomepageUrl = homepageUrl;

            entry.Versions[version] = new VersionRecord
            {
                InstalledFiles = files,
                InstallDate = DateTime.UtcNow
            };

            Save(gameRoot, index);
        }

        /// <summary>
        /// Upserts a mod entry from scan results without overwriting existing version data
        /// Used when detecting mods that were manually installed or installed by other means
        /// </summary>
        public static void UpsertFromScan(string gameRoot, string guid, string friendlyName, string version, List<string> files, string? homepageUrl = null)
        {
            var index = Load(gameRoot);
            
            if (!index.ContainsKey(guid))
            {
                index[guid] = new ModIndexEntry
                {
                    Guid = guid,
                    FriendlyName = friendlyName,
                    Versions = new Dictionary<string, VersionRecord>(StringComparer.OrdinalIgnoreCase)
                };
            }

            var entry = index[guid];
            
            // Update friendly name if provided
            if (!string.IsNullOrEmpty(friendlyName))
                entry.FriendlyName = friendlyName;

            // Update homepage if provided
            if (!string.IsNullOrEmpty(homepageUrl))
                entry.HomepageUrl = homepageUrl;

            // Only add version if it doesn't exist (don't overwrite existing install data)
            if (!entry.Versions.ContainsKey(version))
            {
                entry.Versions[version] = new VersionRecord
                {
                    InstalledFiles = files,
                    InstallDate = DateTime.UtcNow
                };
            }

            Save(gameRoot, index);
        }

        /// <summary>
        /// Gets a mod entry by GUID
        /// </summary>
        public static ModIndexEntry? GetEntry(string gameRoot, string guid)
        {
            var index = Load(gameRoot);
            return index.TryGetValue(guid, out var entry) ? entry : null;
        }

        /// <summary>
        /// Removes a mod completely from the index
        /// </summary>
        public static void RemoveMod(string gameRoot, string guid)
        {
            var index = Load(gameRoot);
            if (index.ContainsKey(guid))
            {
                index.Remove(guid);
                Save(gameRoot, index);
            }
        }

        /// <summary>
        /// Removes a specific version of a mod from the index
        /// If no versions remain, removes the entire mod entry
        /// </summary>
        public static void RemoveVersion(string gameRoot, string guid, string version)
        {
            var index = Load(gameRoot);
            if (index.TryGetValue(guid, out var entry))
            {
                if (entry.Versions.ContainsKey(version))
                {
                    entry.Versions.Remove(version);
                    if (entry.Versions.Count == 0)
                        index.Remove(guid);
                    Save(gameRoot, index);
                }
            }
        }

        /// <summary>
        /// Gets all mod GUIDs in the index
        /// </summary>
        public static List<string> GetAllGuids(string gameRoot)
        {
            var index = Load(gameRoot);
            return new List<string>(index.Keys);
        }

        /// <summary>
        /// Checks if a mod exists in the index
        /// </summary>
        public static bool HasMod(string gameRoot, string guid)
        {
            var index = Load(gameRoot);
            return index.ContainsKey(guid);
        }

        /// <summary>
        /// Gets all versions of a specific mod
        /// </summary>
        public static List<string> GetVersions(string gameRoot, string guid)
        {
            var entry = GetEntry(gameRoot, guid);
            return entry != null ? new List<string>(entry.Versions.Keys) : new List<string>();
        }

        /// <summary>
        /// Gets the installed files for a specific mod version
        /// </summary>
        public static List<string> GetInstalledFiles(string gameRoot, string guid, string version)
        {
            var entry = GetEntry(gameRoot, guid);
            if (entry != null && entry.Versions.TryGetValue(version, out var versionRecord))
            {
                return versionRecord.InstalledFiles;
            }
            return new List<string>();
        }
    }

    /// <summary>
    /// Represents a mod entry in the index
    /// </summary>
    public class ModIndexEntry
    {
        public string Guid { get; set; } = "";
        public string FriendlyName { get; set; } = "";
        public string? HomepageUrl { get; set; }
        public Dictionary<string, VersionRecord> Versions { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Represents a specific version installation of a mod
    /// </summary>
    public class VersionRecord
    {
        public List<string> InstalledFiles { get; set; } = new();
        public DateTime InstallDate { get; set; }
    }
}