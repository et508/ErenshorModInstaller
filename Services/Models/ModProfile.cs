using System;
using System.Collections.Generic;

namespace ErenshorModInstaller.Wpf.Services.Models
{
    public class ModProfile
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        
        public string Name { get; set; } = "";
        
        public string Description { get; set; } = "";
        
        public DateTime Created { get; set; } = DateTime.UtcNow;
        
        public DateTime LastUsed { get; set; } = DateTime.UtcNow;
        
        public List<string> EnabledModGuids { get; set; } = new List<string>();
        
        public ModProfile()
        {
        }
        
        public ModProfile(string name, string description = "")
        {
            Name = name;
            Description = description;
        }
    }
}