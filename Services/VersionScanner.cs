using System;
using System.IO;
using System.Linq;
using Mono.Cecil;

namespace ErenshorModInstaller.Wpf.Services
{
    public static class VersionScanner
    {
        public sealed class ModVersionInfo
        {
            public string Guid { get; set; } = "";
            public string Name { get; set; } = "";
            public string Version { get; set; } = "0.0.0";
            public string DllPath { get; set; } = "";
        }

        public static ModVersionInfo? ScanDll(string filePath)
        {
            if (!File.Exists(filePath)) return null;

            try
            {
                using var asm = AssemblyDefinition.ReadAssembly(
                    filePath,
                    new ReaderParameters
                    {
                        ReadSymbols = false,
                        ReadingMode = ReadingMode.Immediate // safer for attribute reads
                    });

                foreach (var module in asm.Modules)
                {
                    foreach (var type in module.Types)
                    {
                        var info = ScanTypeRecursive(type, filePath);
                        if (info != null) return info;
                    }
                }
            }
            catch
            {
                // unreadable / native / not managed => not a plugin
            }

            return null;
        }

        public static ModVersionInfo? ScanFolder(string folderPath)
        {
            if (!Directory.Exists(folderPath)) return null;

            var dlls = Directory.GetFiles(folderPath, "*.dll", SearchOption.AllDirectories)
                                .Concat(Directory.GetFiles(folderPath, "*.dll.disabled", SearchOption.AllDirectories))
                                .OrderBy(p => DepthFrom(folderPath, p))
                                .ThenBy(p => p, StringComparer.OrdinalIgnoreCase)
                                .ToArray();

            foreach (var dll in dlls)
            {
                var info = ScanDll(dll);
                if (info != null) return info;
            }

            return null;
        }

        private static int DepthFrom(string root, string path)
        {
            var rel = Path.GetRelativePath(root, path);
            return rel.Count(c => c == Path.DirectorySeparatorChar || c == Path.AltDirectorySeparatorChar);
        }

        private static bool IsBepInPluginAttribute(CustomAttribute attr)
        {
            var full = attr.AttributeType.FullName; // e.g., "BepInEx.BepInPlugin"
            var name = attr.AttributeType.Name;     // e.g., "BepInPluginAttribute"
            return full.EndsWith(".BepInPlugin", StringComparison.Ordinal)
                || full.EndsWith(".BepInPluginAttribute", StringComparison.Ordinal)
                || string.Equals(name, "BepInPlugin", StringComparison.Ordinal)
                || string.Equals(name, "BepInPluginAttribute", StringComparison.Ordinal);
        }

        private static ModVersionInfo? ScanTypeRecursive(TypeDefinition type, string filePath)
        {
            foreach (var attr in type.CustomAttributes)
            {
                if (!IsBepInPluginAttribute(attr)) continue;

                if (attr.ConstructorArguments.Count >= 3)
                {
                    var guid = attr.ConstructorArguments[0].Value?.ToString() ?? "";
                    var name = attr.ConstructorArguments[1].Value?.ToString() ?? "";
                    var ver  = attr.ConstructorArguments[2].Value?.ToString() ?? "";

                    if (string.IsNullOrWhiteSpace(ver) || ver == "0.0.0.0") ver = "0.0.0";

                    return new ModVersionInfo
                    {
                        Guid = guid.Trim(),
                        Name = string.IsNullOrWhiteSpace(name) ? guid.Trim() : name.Trim(),
                        Version = ver.Trim(),
                        DllPath = filePath
                    };
                }
            }

            if (type.HasNestedTypes)
            {
                foreach (var nested in type.NestedTypes)
                {
                    var fromNested = ScanTypeRecursive(nested, filePath);
                    if (fromNested != null) return fromNested;
                }
            }

            return null;
        }
    }
}
