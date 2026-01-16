using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace ErenshorModInstaller.Wpf.Services
{
    public static class ModIndex
    {
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };

        private static string GetIndexPath(string gameRoot)
        {
            return AppDataManager.GetModIndexPath();
        }
        
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
        
        public static void EnsureMinimalIndex(string gameRoot)
        {
            AppDataManager.EnsureAppDataFolder();
            
            var path = GetIndexPath(gameRoot);
            if (!File.Exists(path))
            {
                Save(gameRoot, new Dictionary<string, ModIndexEntry>(StringComparer.OrdinalIgnoreCase));
            }
        }
        
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
            
            if (!string.IsNullOrEmpty(friendlyName))
                entry.FriendlyName = friendlyName;
            
            if (!string.IsNullOrEmpty(homepageUrl))
                entry.HomepageUrl = homepageUrl;
            
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
        
        public static ModIndexEntry? GetEntry(string gameRoot, string guid)
        {
            var index = Load(gameRoot);
            return index.TryGetValue(guid, out var entry) ? entry : null;
        }
        
        public static void RemoveMod(string gameRoot, string guid)
        {
            var index = Load(gameRoot);
            if (index.ContainsKey(guid))
            {
                index.Remove(guid);
                Save(gameRoot, index);
            }
        }
        
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
        
        public static List<string> GetAllGuids(string gameRoot)
        {
            var index = Load(gameRoot);
            return new List<string>(index.Keys);
        }
        
        public static bool HasMod(string gameRoot, string guid)
        {
            var index = Load(gameRoot);
            return index.ContainsKey(guid);
        }
        
        public static List<string> GetVersions(string gameRoot, string guid)
        {
            var entry = GetEntry(gameRoot, guid);
            return entry != null ? new List<string>(entry.Versions.Keys) : new List<string>();
        }
        
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
    
    public class ModIndexEntry
    {
        public string Guid { get; set; } = "";
        public string FriendlyName { get; set; } = "";
        public string? HomepageUrl { get; set; }
        public Dictionary<string, VersionRecord> Versions { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }
    
    public class VersionRecord
    {
        public List<string> InstalledFiles { get; set; } = new();
        public DateTime InstallDate { get; set; }
    }
}