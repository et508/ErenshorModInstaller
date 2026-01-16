using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace ErenshorModInstaller.Wpf.Services
{
    public static class AppDataManager
    {
        private static readonly string AppDataFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ErenshorModInstaller"
        );

        private static readonly string SettingsFilePath = Path.Combine(AppDataFolder, "settings.json");
        private static readonly string ModIndexFileName = "ModIndex.json";
        
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };
        
        public static string GetAppDataFolder() => AppDataFolder;
        
        public static string GetModIndexPath() => Path.Combine(AppDataFolder, ModIndexFileName);
        
        public static void EnsureAppDataFolder()
        {
            try
            {
                if (!Directory.Exists(AppDataFolder))
                {
                    Directory.CreateDirectory(AppDataFolder);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to initialize AppData folder: {ex.Message}");
            }
        }

        private static void CreateDefaultSettings()
        {
            try
            {
                var defaultSettings = new AppSettings
                {
                    GameInstallPath = "",
                    GameInstallPaths = new List<GameInstallPathEntry>(),
                    LastUsedVersion = "",
                    AutoDetectOnStartup = true,
                    LastUpdated = DateTime.UtcNow
                };

                var json = JsonSerializer.Serialize(defaultSettings, JsonOptions);
                File.WriteAllText(SettingsFilePath, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to create default settings: {ex.Message}");
            }
        }
        
        public static AppSettings LoadSettings()
        {
            EnsureAppDataFolder();

            try
            {
                if (!File.Exists(SettingsFilePath))
                {
                    CreateDefaultSettings();
                }

                if (File.Exists(SettingsFilePath))
                {
                    var json = File.ReadAllText(SettingsFilePath);
                    var settings = JsonSerializer.Deserialize<AppSettings>(json, JsonOptions);
                    return settings ?? new AppSettings();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to load settings: {ex.Message}");
            }

            return new AppSettings();
        }
        
        public static void SaveSettings(AppSettings settings)
        {
            EnsureAppDataFolder();

            try
            {
                settings.LastUpdated = DateTime.UtcNow;
                var json = JsonSerializer.Serialize(settings, JsonOptions);
                File.WriteAllText(SettingsFilePath, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to save settings: {ex.Message}");
            }
        }
        
        public static string GetGameInstallPath()
        {
            var settings = LoadSettings();
            return settings.GameInstallPath ?? "";
        }
        
        public static void SaveGameInstallPath(string path, string label = "Custom")
        {
            var settings = LoadSettings();
            settings.GameInstallPath = path;
            
            if (!string.IsNullOrWhiteSpace(path))
            {
                var existingEntry = settings.GameInstallPaths.FirstOrDefault(p => 
                    string.Equals(p.Path, path, StringComparison.OrdinalIgnoreCase));
                
                if (existingEntry == null)
                {
                    settings.GameInstallPaths.Add(new GameInstallPathEntry
                    {
                        Path = path,
                        Label = label,
                        AddedDate = DateTime.UtcNow
                    });
                }
            }
            
            SaveSettings(settings);
        }
        
        public static List<GameInstallPathEntry> GetGameInstallPathEntries()
        {
            var settings = LoadSettings();
            var entries = new List<GameInstallPathEntry>();
            
            foreach (var entry in settings.GameInstallPaths)
            {
                if (!string.IsNullOrEmpty(entry.Path) && Directory.Exists(entry.Path))
                {
                    entries.Add(entry);
                }
            }

            return entries;
        }
        
        public static List<string> GetGameInstallPaths()
        {
            return GetGameInstallPathEntries().Select(e => e.Path).ToList();
        }
        
        public static void AddGameInstallPath(string path, string label = "Custom")
        {
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
                return;

            var settings = LoadSettings();
            
            var existingEntry = settings.GameInstallPaths.FirstOrDefault(p => 
                string.Equals(p.Path, path, StringComparison.OrdinalIgnoreCase));

            if (existingEntry != null)
            {
                existingEntry.Label = label;
            }
            else
            {
                settings.GameInstallPaths.Add(new GameInstallPathEntry
                {
                    Path = path,
                    Label = label,
                    AddedDate = DateTime.UtcNow
                });
            }
            
            if (string.IsNullOrEmpty(settings.GameInstallPath))
            {
                settings.GameInstallPath = path;
            }

            SaveSettings(settings);
        }
        
        public static void UpdatePathLabel(string path, string newLabel)
        {
            if (string.IsNullOrWhiteSpace(path))
                return;

            var settings = LoadSettings();
            var entry = settings.GameInstallPaths.FirstOrDefault(p => 
                string.Equals(p.Path, path, StringComparison.OrdinalIgnoreCase));

            if (entry != null)
            {
                entry.Label = newLabel;
                SaveSettings(settings);
            }
        }
        
        public static void RemoveGameInstallPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return;

            var settings = LoadSettings();
            
            settings.GameInstallPaths.RemoveAll(p => 
                string.Equals(p.Path, path, StringComparison.OrdinalIgnoreCase));
            
            if (string.Equals(settings.GameInstallPath, path, StringComparison.OrdinalIgnoreCase))
            {
                settings.GameInstallPath = settings.GameInstallPaths.Count > 0 
                    ? settings.GameInstallPaths[0].Path 
                    : "";
            }

            SaveSettings(settings);
        }
        
        public static void ClearAllSettings()
        {
            try
            {
                if (File.Exists(SettingsFilePath))
                {
                    File.Delete(SettingsFilePath);
                }
                CreateDefaultSettings();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to clear settings: {ex.Message}");
            }
        }
        
        public static string? GetActiveProfileId()
        {
            var settings = LoadSettings();
            return settings.ActiveProfileId;
        }
        
        public static void SetActiveProfileId(string? profileId)
        {
            var settings = LoadSettings();
            settings.ActiveProfileId = profileId;
            SaveSettings(settings);
        }
    }
    
    public class AppSettings
    {
        public string GameInstallPath { get; set; } = "";
        public List<GameInstallPathEntry> GameInstallPaths { get; set; } = new List<GameInstallPathEntry>();
        public string LastUsedVersion { get; set; } = "";
        public bool AutoDetectOnStartup { get; set; } = true;
        public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
        
        public string? ActiveProfileId { get; set; } = null;
    }
    
    public class GameInstallPathEntry
    {
        public string Path { get; set; } = "";
        public string Label { get; set; } = "Custom";
        public DateTime AddedDate { get; set; } = DateTime.UtcNow;
    }
}