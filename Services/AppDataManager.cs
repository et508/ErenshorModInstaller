using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace ErenshorModInstaller.Wpf.Services
{
    /// <summary>
    /// Manages application data storage in %AppData%\ErenshorModInstaller
    /// Handles settings.json, ModIndex.json, and other persistent files
    /// </summary>
    public static class AppDataManager
    {
        private static readonly string AppDataFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ErenshorModInstaller"
        );

        private static readonly string SettingsFilePath = Path.Combine(AppDataFolder, "settings.json");
        private static readonly string ModIndexFileName = "ModIndex.json";

        // JSON serialization options
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };

        /// <summary>
        /// Gets the full path to the ErenshorModInstaller AppData folder
        /// </summary>
        public static string GetAppDataFolder() => AppDataFolder;

        /// <summary>
        /// Gets the full path to the ModIndex.json file location
        /// </summary>
        public static string GetModIndexPath() => Path.Combine(AppDataFolder, ModIndexFileName);

        /// <summary>
        /// Ensures the AppData folder exists (does NOT create default files to avoid recursion)
        /// </summary>
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

        /// <summary>
        /// Loads all settings from settings.json
        /// </summary>
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

        /// <summary>
        /// Saves all settings to settings.json
        /// </summary>
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

        /// <summary>
        /// Gets the saved game install path (primary path)
        /// </summary>
        public static string GetGameInstallPath()
        {
            var settings = LoadSettings();
            return settings.GameInstallPath ?? "";
        }

        /// <summary>
        /// Saves the game install path (primary path)
        /// </summary>
        public static void SaveGameInstallPath(string path, string label = "Custom")
        {
            var settings = LoadSettings();
            settings.GameInstallPath = path;
            
            // Add to paths list if not already there
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

        /// <summary>
        /// Gets all saved game install path entries with labels
        /// </summary>
        public static List<GameInstallPathEntry> GetGameInstallPathEntries()
        {
            var settings = LoadSettings();
            var entries = new List<GameInstallPathEntry>();

            // Return all entries that have valid paths
            foreach (var entry in settings.GameInstallPaths)
            {
                if (!string.IsNullOrEmpty(entry.Path) && Directory.Exists(entry.Path))
                {
                    entries.Add(entry);
                }
            }

            return entries;
        }

        /// <summary>
        /// Gets all saved game install paths (just the paths, no labels)
        /// </summary>
        public static List<string> GetGameInstallPaths()
        {
            return GetGameInstallPathEntries().Select(e => e.Path).ToList();
        }

        /// <summary>
        /// Adds a game install path to the saved paths with a label
        /// </summary>
        public static void AddGameInstallPath(string path, string label = "Custom")
        {
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
                return;

            var settings = LoadSettings();

            // Check if path already exists
            var existingEntry = settings.GameInstallPaths.FirstOrDefault(p => 
                string.Equals(p.Path, path, StringComparison.OrdinalIgnoreCase));

            if (existingEntry != null)
            {
                // Update existing entry's label
                existingEntry.Label = label;
            }
            else
            {
                // Add new entry
                settings.GameInstallPaths.Add(new GameInstallPathEntry
                {
                    Path = path,
                    Label = label,
                    AddedDate = DateTime.UtcNow
                });
            }

            // If no primary path set, make this the primary
            if (string.IsNullOrEmpty(settings.GameInstallPath))
            {
                settings.GameInstallPath = path;
            }

            SaveSettings(settings);
        }

        /// <summary>
        /// Updates the label for an existing game install path
        /// </summary>
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

        /// <summary>
        /// Removes a game install path from saved paths
        /// </summary>
        public static void RemoveGameInstallPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return;

            var settings = LoadSettings();

            // Remove from paths list
            settings.GameInstallPaths.RemoveAll(p => 
                string.Equals(p.Path, path, StringComparison.OrdinalIgnoreCase));

            // If this was the primary path, set a new primary from the list
            if (string.Equals(settings.GameInstallPath, path, StringComparison.OrdinalIgnoreCase))
            {
                settings.GameInstallPath = settings.GameInstallPaths.Count > 0 
                    ? settings.GameInstallPaths[0].Path 
                    : "";
            }

            SaveSettings(settings);
        }

        /// <summary>
        /// Clears all settings (for testing or reset functionality)
        /// </summary>
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
    }

    /// <summary>
    /// Application settings model
    /// </summary>
    public class AppSettings
    {
        public string GameInstallPath { get; set; } = "";
        public List<GameInstallPathEntry> GameInstallPaths { get; set; } = new List<GameInstallPathEntry>();
        public string LastUsedVersion { get; set; } = "";
        public bool AutoDetectOnStartup { get; set; } = true;
        public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
    }

    /// <summary>
    /// Represents a game install path with a user-friendly label
    /// </summary>
    public class GameInstallPathEntry
    {
        public string Path { get; set; } = "";
        public string Label { get; set; } = "Custom";
        public DateTime AddedDate { get; set; } = DateTime.UtcNow;
    }
}