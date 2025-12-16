using System;
using System.IO;

namespace ErenshorModInstaller.Wpf.Services
{
    internal static class DllStateManager
    {
        private const string DisabledExtension = ".disabled";

        public static void EnableDll(string dllPath)
        {
            if (!dllPath.EndsWith(DisabledExtension, StringComparison.OrdinalIgnoreCase))
                return;

            var enabledPath = dllPath[..^DisabledExtension.Length];
            if (File.Exists(dllPath))
            {
                if (File.Exists(enabledPath)) File.Delete(enabledPath);
                File.Move(dllPath, enabledPath);
            }
        }

        public static void DisableDll(string dllPath)
        {
            if (dllPath.EndsWith(DisabledExtension, StringComparison.OrdinalIgnoreCase))
                return;

            var disabledPath = dllPath + DisabledExtension;
            if (File.Exists(dllPath))
            {
                if (File.Exists(disabledPath)) File.Delete(disabledPath);
                File.Move(dllPath, disabledPath);
            }
        }

        public static void EnableAllInFolder(string rootDir)
        {
            try
            {
                foreach (var disabled in Directory.GetFiles(rootDir, $"*{DisabledExtension}", SearchOption.AllDirectories))
                {
                    try
                    {
                        EnableDll(disabled);
                    }
                    catch { }
                }
            }
            catch { }
        }

        public static void DisableAllInFolder(string rootDir)
        {
            try
            {
                foreach (var dll in Directory.GetFiles(rootDir, "*.dll", SearchOption.AllDirectories))
                {
                    var disabled = dll + DisabledExtension;
                    try
                    {
                        if (File.Exists(disabled))
                        {
                            File.Delete(dll);
                            continue;
                        }
                        File.Move(dll, disabled);
                    }
                    catch { }
                }
            }
            catch { }
        }

        public static bool IsEnabled(string dllPath)
        {
            return !dllPath.EndsWith(DisabledExtension, StringComparison.OrdinalIgnoreCase);
        }

        public static string GetEnabledPath(string basePath)
        {
            return basePath.EndsWith(DisabledExtension, StringComparison.OrdinalIgnoreCase)
                ? basePath[..^DisabledExtension.Length]
                : basePath;
        }

        public static string? FindDllPath(string pluginsDir, string dllFileName)
        {
            var enabledPath = Path.Combine(pluginsDir, dllFileName);
            var disabledPath = enabledPath + DisabledExtension;

            return File.Exists(enabledPath) ? enabledPath
                : (File.Exists(disabledPath) ? disabledPath : null);
        }
    }
}
