using System;

namespace ErenshorModInstaller.Wpf.Services.Models
{
    public sealed class ModItem
    {
        public string Guid { get; set; } = "";
        public string DisplayName { get; set; } = "";  
        public string Version { get; set; } = "";      
        public bool IsFolder { get; set; }
        public string? FolderName { get; set; }     
        public string? DllFileName { get; set; }    
        public string PluginDllFullPath { get; set; } = "";

        public bool IsEnabled { get; set; }

        public string StatusSuffix => IsEnabled ? "" : " (disabled)";
    }
}