using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using ErenshorModInstaller.Wpf.Services.Models;

namespace ErenshorModInstaller.Wpf.Services
{
    public static class ProfileManager
    {
        private static readonly string ProfilesDirectory = 
            Path.Combine(AppDataManager.GetAppDataFolder(), "Profiles");

        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };
        
        private static void EnsureProfilesDirectory()
        {
            if (!Directory.Exists(ProfilesDirectory))
            {
                Directory.CreateDirectory(ProfilesDirectory);
            }
        }
        
        public static List<ModProfile> GetAllProfiles()
        {
            var profiles = new List<ModProfile>();

            if (!Directory.Exists(ProfilesDirectory))
                return profiles;

            foreach (var file in Directory.GetFiles(ProfilesDirectory, "*.json"))
            {
                try
                {
                    var json = File.ReadAllText(file);
                    var profile = JsonSerializer.Deserialize<ModProfile>(json, JsonOptions);
                    if (profile != null)
                    {
                        profiles.Add(profile);
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Error loading profile {file}: {ex.Message}");
                }
            }

            return profiles.OrderByDescending(p => p.LastUsed).ToList();
        }
        
        public static ModProfile? GetProfile(string profileId)
        {
            if (string.IsNullOrWhiteSpace(profileId))
                return null;

            var profiles = GetAllProfiles();
            return profiles.FirstOrDefault(p => p.Id == profileId);
        }
        
        public static ModProfile? GetProfileByName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return null;

            var profiles = GetAllProfiles();
            return profiles.FirstOrDefault(p => 
                string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));
        }
        
        public static void SaveProfile(ModProfile profile)
        {
            if (profile == null)
                throw new ArgumentNullException(nameof(profile));

            EnsureProfilesDirectory();

            var fileName = GetSafeFileName(profile.Name) + "_" + profile.Id + ".json";
            var filePath = Path.Combine(ProfilesDirectory, fileName);

            var json = JsonSerializer.Serialize(profile, JsonOptions);
            File.WriteAllText(filePath, json);
        }
        
        public static ModProfile CreateProfileFromMods(string name, string description, IEnumerable<ModItem> mods)
        {
            var profile = new ModProfile(name, description);
            
            profile.EnabledModGuids = mods
                .Where(m => m.IsEnabled)
                .Select(m => m.Guid)
                .ToList();

            SaveProfile(profile);
            return profile;
        }
        
        public static void ApplyProfile(ModProfile profile, IEnumerable<ModItem> mods)
        {
            if (profile == null)
                throw new ArgumentNullException(nameof(profile));

            var modList = mods.ToList();

            foreach (var mod in modList)
            {
                bool shouldBeEnabled = profile.EnabledModGuids.Contains(mod.Guid);

                if (shouldBeEnabled && !mod.IsEnabled)
                {
                    ModService.Enable(mod);
                }
                else if (!shouldBeEnabled && mod.IsEnabled)
                {
                    ModService.Disable(mod);
                }
            }
            
            profile.LastUsed = DateTime.UtcNow;
            SaveProfile(profile);
        }
        
        public static bool DeleteProfile(string profileId)
        {
            if (string.IsNullOrWhiteSpace(profileId))
                return false;

            var profile = GetProfile(profileId);
            if (profile == null)
                return false;

            var fileName = GetSafeFileName(profile.Name) + "_" + profile.Id + ".json";
            var filePath = Path.Combine(ProfilesDirectory, fileName);

            if (File.Exists(filePath))
            {
                File.Delete(filePath);
                return true;
            }

            return false;
        }
        
        public static bool RenameProfile(string profileId, string newName)
        {
            if (string.IsNullOrWhiteSpace(profileId) || string.IsNullOrWhiteSpace(newName))
                return false;

            var profile = GetProfile(profileId);
            if (profile == null)
                return false;
            
            if (GetProfileByName(newName) != null)
                return false;
            
            var oldFileName = GetSafeFileName(profile.Name) + "_" + profile.Id + ".json";
            var oldFilePath = Path.Combine(ProfilesDirectory, oldFileName);
            
            profile.Name = newName;
            
            SaveProfile(profile);
            
            var newFileName = GetSafeFileName(newName) + "_" + profile.Id + ".json";
            if (oldFileName != newFileName && File.Exists(oldFilePath))
            {
                try
                {
                    File.Delete(oldFilePath);
                }
                catch
                {
                    // If deletion fails, not critical - new file already saved
                }
            }

            return true;
        }
        
        public static bool ProfileNameExists(string name, string? excludeId = null)
        {
            if (string.IsNullOrWhiteSpace(name))
                return false;

            var profiles = GetAllProfiles();
            return profiles.Any(p => 
                string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase) && 
                p.Id != excludeId);
        }
        
        private static string GetSafeFileName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return "profile";

            var invalid = Path.GetInvalidFileNameChars();
            var sanitized = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
            
            if (sanitized.Length > 50)
                sanitized = sanitized.Substring(0, 50);

            return sanitized;
        }
        
        public static ProfileStats GetProfileStats(ModProfile profile, IEnumerable<ModItem> currentMods)
        {
            var stats = new ProfileStats();
            var modList = currentMods.ToList();

            stats.TotalModsInProfile = profile.EnabledModGuids.Count;
            stats.ModsFound = profile.EnabledModGuids.Count(guid => 
                modList.Any(m => m.Guid == guid));
            stats.ModsMissing = stats.TotalModsInProfile - stats.ModsFound;

            return stats;
        }
        
        public class ProfileStats
        {
            public int TotalModsInProfile { get; set; }
            public int ModsFound { get; set; }
            public int ModsMissing { get; set; }
        }
    }
}
