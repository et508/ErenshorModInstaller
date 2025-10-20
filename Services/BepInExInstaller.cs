using System;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace ErenshorModInstaller.Wpf.Services
{
    public static class BepInExInstaller
    {
        private const string ReleasesApi = "https://api.github.com/repos/et508/Erenshor.BepInEx/releases/tags/e1";
        
        public static async Task<bool> InstallLatestBepInEx5WindowsX64Async(
            string gameRoot,
            IProgress<string>? progress = null,
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(gameRoot) || !Directory.Exists(gameRoot))
                throw new DirectoryNotFoundException("Game root folder invalid.");

            progress?.Report("Checking latest BepInEx release…");

            using var http = new HttpClient();
            http.DefaultRequestHeaders.UserAgent.ParseAdd("ErenshorModInstaller/1.0");
            http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");

            using var resp = await http.GetAsync(ReleasesApi, ct);
            resp.EnsureSuccessStatusCode();
            var json = await resp.Content.ReadAsStringAsync(ct);

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            string? assetName = null;
            string? assetUrl  = null;
            
            var rx = new Regex(@"^BepInEx_win_x64_5\.4\.23\.4e\.zip$", RegexOptions.IgnoreCase);
            if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
            {
                foreach (var a in assets.EnumerateArray())
                {
                    var name = a.GetProperty("name").GetString();
                    var url  = a.GetProperty("browser_download_url").GetString();
                    if (!string.IsNullOrWhiteSpace(name) && rx.IsMatch(name!) && !string.IsNullOrWhiteSpace(url))
                    {
                        assetName = name;
                        assetUrl  = url;
                        break;
                    }
                }
            }

            if (assetUrl is null)
                throw new InvalidOperationException("Could not find BepInEx 5 x64 ZIP in the latest release.");

            progress?.Report($"Downloading {assetName}…");
            var tmpZip = Path.Combine(Path.GetTempPath(), assetName!);
            
            using (var download = await http.GetAsync(assetUrl, HttpCompletionOption.ResponseHeadersRead, ct))
            {
                download.EnsureSuccessStatusCode();
                await using var fs = File.Create(tmpZip);
                await download.Content.CopyToAsync(fs, ct);
            }
            
            progress?.Report("Extracting BepInEx…");

            using (var archive = ZipFile.OpenRead(tmpZip))
            {
                foreach (var entry in archive.Entries)
                {
                    ct.ThrowIfCancellationRequested();

                    var fullPath = Path.GetFullPath(Path.Combine(gameRoot, entry.FullName));
                    if (!fullPath.StartsWith(Path.GetFullPath(gameRoot), StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("Blocked unsafe ZIP path.");

                    var dir = Path.GetDirectoryName(fullPath);
                    if (!string.IsNullOrEmpty(dir))
                        Directory.CreateDirectory(dir);

                    if (string.IsNullOrEmpty(entry.Name))
                    {
                        Directory.CreateDirectory(fullPath);
                    }
                    else
                    {
                        entry.ExtractToFile(fullPath, overwrite: true);
                    }
                }
            }

            try { File.Delete(tmpZip); } catch { /* ignore temp cleanup */ }
            
            var bepDir    = Installer.GetBepInExDir(gameRoot);
            var winHttp   = Path.Combine(gameRoot, "winhttp.dll");
            var doorstop  = Path.Combine(gameRoot, "doorstop_config.ini");

            if (!Directory.Exists(bepDir) || !File.Exists(winHttp) || !File.Exists(doorstop))
                throw new InvalidOperationException("BepInEx files were not extracted correctly.");

            progress?.Report("BepInEx installed.");
            return true;
        }
    }
}
