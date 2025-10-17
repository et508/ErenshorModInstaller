using System;

namespace ErenshorModInstaller.Wpf.Services.Models
{
    public sealed class ModItem
    {
        public string Guid { get; set; } = "";
        public string DisplayName { get; set; } = "";   // BepInPlugin Name
        public string Version { get; set; } = "";       // BepInPlugin Version (or "0.0.0")
        public bool IsFolder { get; set; }

        public string? FolderName { get; set; }         // if folder mod
        public string? DllFileName { get; set; }        // if top-level dll mod (baseName.dll)

        // Absolute path to the plugin dll (can be .dll or .dll.disabled)
        public string PluginDllFullPath { get; set; } = "";

        public bool IsEnabled { get; set; }

        public string StatusSuffix => IsEnabled ? "" : " (disabled)";
    }
}