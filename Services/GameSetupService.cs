using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using ErenshorModInstaller.Wpf.Services.Abstractions;
using ErenshorModInstaller.Wpf.UI;

namespace ErenshorModInstaller.Wpf.Services
{
    /// <summary>
    /// Centralized BepInEx validation, auto-install, first-run orchestration,
    /// and config fix prompts. All user prompts live here.
    /// </summary>
    public static class GameSetupService
    {
        public static async Task<bool> ValidateAndFixAsync(string gameRoot, IStatusSink? status)
        {
            if (string.IsNullOrWhiteSpace(gameRoot) || !Directory.Exists(gameRoot))
            {
                status?.Warn("Set a valid game folder first.");
                return false;
            }

            // 1) BepInEx present?
            string? ver = null;
            try
            {
                ver = Installer.ValidateBepInExOrThrow(gameRoot);
            }
            catch (Exception ex)
            {
                status?.Warn(ex.Message);

                var doInstall = Prompts.ShowBepInExInstall();

                if (doInstall != PromptResult.Primary) return false;

                try
                {
                    // adapt IStatusSink -> IProgress<string> for installer
                    var progress = new Progress<string>(s => status?.Info(s));
                    await BepInExInstaller.InstallLatestBepInEx5WindowsX64Async(
                        gameRoot,
                        progress,
                        CancellationToken.None);

                    ver = Installer.ValidateBepInExOrThrow(gameRoot);
                    status?.Info($"BepInEx installed. ({ver ?? "version unknown"})");
                }
                catch (Exception ex2)
                {
                    MessageBox.Show(
                        "Automatic install failed:\n" + ex2.Message,
                        "BepInEx Install Failed",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                    status?.Error("BepInEx install failed: " + ex2.Message);
                    return false;
                }
            }

            // 2) plugins folder present?
            var plugins = Installer.GetPluginsDir(gameRoot);
            if (!Directory.Exists(plugins))
            {
                status?.Warn("Run Erenshor once to complete BepInEx setup.");

                var runNow = Prompts.ShowBepInExSetup();

                if (runNow == PromptResult.Primary)
                {
                    await LaunchErenshorForSetupAsync(gameRoot, status);
                }
            }
            else
            {
                status?.Info($"BepInEx OK ({ver ?? "version unknown"})");
            }

            // 3) Config checks
            var cfgStatus = Installer.GetBepInExConfigStatus(gameRoot, out var cfgPath);
            switch (cfgStatus)
            {
                case Installer.BepInExConfigStatus.Ok:
                    break;

                case Installer.BepInExConfigStatus.MissingConfig:
                    status?.Warn("Run Erenshor once to complete BepInEx setup.");
                    {
                        var runNowCfg = Prompts.ShowBepInExSetup();

                        if (runNowCfg == PromptResult.Primary)
                        {
                            await LaunchErenshorForSetupAsync(gameRoot, status);
                        }
                    }
                    break;

                case Installer.BepInExConfigStatus.MissingKey:
                case Installer.BepInExConfigStatus.WrongValue:
                    status?.Warn("Warning: HideManagerGameObject should be TRUE for Erenshor mods to work correctly.");

                    var fix = Prompts.ShowBepInExConfigFix();

                    if (fix == PromptResult.Primary)
                    {
                        try
                        {
                            Installer.EnsureHideManagerGameObjectTrue(gameRoot);
                            status?.Info("Updated BepInEx.cfg: HideManagerGameObject = true.");
                        }
                        catch (Exception ex2)
                        {
                            status?.Error("Failed to update config: " + ex2.Message);
                        }
                    }
                    break;
            }

            return true;
        }

        // ---------- First-run orchestration ----------

        public static async Task LaunchErenshorForSetupAsync(string root, IStatusSink? status)
        {
            var exe = Path.Combine(root, "Erenshor.exe");
            if (!File.Exists(exe))
            {
                MessageBox.Show("Erenshor.exe not found in this folder.", "Launch Erenshor", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            status?.Info("Launching Erenshor to complete BepInEx setup…");

            var proc = Process.Start(new ProcessStartInfo
            {
                FileName = exe,
                WorkingDirectory = root,
                UseShellExecute = true
            });

            if (proc == null)
            {
                status?.Error("Failed to start Erenshor.");
                return;
            }

            using var cts = new CancellationTokenSource();
            var ok = await WaitForBepInExSetupAsync(root, timeout: TimeSpan.FromMinutes(3), status, cts.Token);

            if (ok || IsBepInExSetupComplete(root))
            {
                status?.Info("BepInEx setup complete. Closing Erenshor…");
                TryCloseErenshorProcess(proc, status);
                await Task.Delay(1200);
                await RevalidateAndCongratulateAsync(root, status);
            }
            else
            {
                status?.Warn("BepInEx setup not detected within timeout. You may close Erenshor manually and try again.");
            }
        }

        private static bool IsBepInExSetupComplete(string root)
        {
            var plugins = Installer.GetPluginsDir(root);
            var cfg = Path.Combine(Installer.GetBepInExDir(root), "config", "BepInEx.cfg");
            return Directory.Exists(plugins) && File.Exists(cfg);
        }

        private static async Task<bool> WaitForBepInExSetupAsync(string root, TimeSpan timeout, IStatusSink? status, CancellationToken ct)
        {
            var bepRoot = Installer.GetBepInExDir(root);
            var cfgPath = Path.Combine(bepRoot, "config", "BepInEx.cfg");
            var plugins = Installer.GetPluginsDir(root);
            var logPath = Path.Combine(bepRoot, "LogOutput.log");

            var started = DateTime.UtcNow;
            status?.Info("Waiting for BepInEx first-run to finish…");

            while ((DateTime.UtcNow - started) < timeout)
            {
                ct.ThrowIfCancellationRequested();

                var hasPlugins = Directory.Exists(plugins);
                var hasCfg = File.Exists(cfgPath);
                var sawChainloader = false;

                try
                {
                    if (File.Exists(logPath))
                    {
                        var text = File.ReadAllText(logPath);
                        if (text.IndexOf("Chainloader", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            text.IndexOf("BepInEx", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            sawChainloader = true;
                        }
                    }
                }
                catch { }

                if ((hasPlugins && hasCfg) || (hasCfg && sawChainloader))
                {
                    status?.Info("BepInEx setup detected.");
                    return true;
                }

                await Task.Delay(1000, ct);
            }

            status?.Warn("Timed out waiting for BepInEx setup.");
            return false;
        }

        private static void TryCloseErenshorProcess(Process proc, IStatusSink? status)
        {
            try
            {
                if (proc.HasExited) return;

                if (proc.MainWindowHandle != IntPtr.Zero)
                {
                    proc.CloseMainWindow();
                    if (proc.WaitForExit(8000)) return;
                }

                var confirm = Prompts.ShowErenshorForceClose();

                if (confirm == PromptResult.Primary)
                {
                    proc.Kill(entireProcessTree: true);
                }
            }
            catch (Exception ex)
            {
                status?.Warn("Failed to close Erenshor: " + ex.Message);
            }
        }

        private static async Task RevalidateAndCongratulateAsync(string root, IStatusSink? status)
        {
            try
            {
                var ver = Installer.ValidateBepInExOrThrow(root);
                var plugins = Installer.GetPluginsDir(root);
                var cfg = Path.Combine(Installer.GetBepInExDir(root), "config", "BepInEx.cfg");

                if (Directory.Exists(plugins) && File.Exists(cfg))
                {
                    status?.Info($"BepInEx OK ({ver ?? "version unknown"})");
                    Prompts.ShowBepInExSuccess();
                }
                else
                {
                    status?.Warn("BepInEx validation incomplete after first run.");
                }
            }
            catch (Exception ex)
            {
                status?.Error("Post-run validation failed: " + ex.Message);
            }

            await Task.CompletedTask;
        }
    }
}
