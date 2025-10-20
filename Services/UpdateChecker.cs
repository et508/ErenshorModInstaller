using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;

namespace ErenshorModInstaller.Wpf.Services
{
    public static class UpdateChecker
    {
        private const string Owner = "et508";
        private const string Repo  = "ErenshorModInstaller";

        private static readonly HttpClient _http = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(8)
        };

        public sealed class LatestInfo
        {
            public string Tag { get; init; } = "";
            public string Name { get; init; } = "";
            public string HtmlUrl { get; init; } = "";
        }

        public static string GetCurrentVersion()
        {
            var asm = Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly();
            var info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
            if (!string.IsNullOrWhiteSpace(info))
            {
                var plus = info.IndexOf('+');
                if (plus > 0) info = info.Substring(0, plus);
                return info.Trim();
            }
            
            var fileVer = asm.GetCustomAttribute<AssemblyFileVersionAttribute>()?.Version;
            if (!string.IsNullOrWhiteSpace(fileVer)) return fileVer.Trim();

            var asmVer = asm.GetName().Version?.ToString();
            return string.IsNullOrWhiteSpace(asmVer) ? "0.0.0" : asmVer!;
        }

        public static async Task<(bool hasUpdate, LatestInfo? latest, string reason)> CheckAsync()
        {
            try
            {
                var current = NormalizeVersion(GetCurrentVersion());
                if (string.IsNullOrWhiteSpace(current))
                    current = "0.0.0";
                
                _http.DefaultRequestHeaders.UserAgent.Clear();
                _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("ErenshorModInstaller", current));
                _http.DefaultRequestHeaders.Accept.Clear();
                _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));

                var url = $"https://api.github.com/repos/{Owner}/{Repo}/releases/latest";
                using var res = await _http.GetAsync(url);
                if (!res.IsSuccessStatusCode)
                    return (false, null, $"HTTP {((int)res.StatusCode)}");

                using var s = await res.Content.ReadAsStreamAsync();
                using var doc = await JsonDocument.ParseAsync(s);
                var root = doc.RootElement;

                var tag = root.GetProperty("tag_name").GetString() ?? "";
                var name = root.TryGetProperty("name", out var nEl) ? (nEl.GetString() ?? tag) : tag;
                var html = root.TryGetProperty("html_url", out var hEl) ? (hEl.GetString() ?? "") : "";

                var normalizedTag = NormalizeVersion(tag);
                if (string.IsNullOrWhiteSpace(normalizedTag))
                    return (false, new LatestInfo { Tag = tag, Name = name, HtmlUrl = html }, "Empty remote tag");
                
                var cmp = CompareSemVerSafe(normalizedTag, current);
                var hasUpdate = cmp > 0;

                return (hasUpdate, new LatestInfo { Tag = normalizedTag, Name = name, HtmlUrl = html },
                        $"current={current}, remote={normalizedTag}, cmp={cmp}");
            }
            catch (Exception ex)
            {
                return (false, null, ex.Message);
            }
        }

        private static string NormalizeVersion(string? v)
        {
            if (string.IsNullOrWhiteSpace(v)) return "";
            v = v.Trim();
            if (v.StartsWith("v", StringComparison.OrdinalIgnoreCase))
                v = v.Substring(1);
            var plus = v.IndexOf('+');
            if (plus > 0) v = v.Substring(0, plus);
            return v;
        }

        private static int CompareSemVerSafe(string a, string b)
        {
            string norm(string s) => string.IsNullOrWhiteSpace(s) ? "0.0.0" : s.Trim();
            var asv = norm(a).Split(new[] { '.', '-', '_' }, StringSplitOptions.RemoveEmptyEntries);
            var bsv = norm(b).Split(new[] { '.', '-', '_' }, StringSplitOptions.RemoveEmptyEntries);

            int n = Math.Max(asv.Length, bsv.Length);
            for (int i = 0; i < n; i++)
            {
                var pa = i < asv.Length ? asv[i] : "0";
                var pb = i < bsv.Length ? bsv[i] : "0";

                if (int.TryParse(pa, out var ia) && int.TryParse(pb, out var ib))
                {
                    if (ia != ib) return ia.CompareTo(ib);
                    continue;
                }

                var cmp = string.Compare(pa, pb, StringComparison.OrdinalIgnoreCase);
                if (cmp != 0) return cmp;
            }
            return 0;
        }
    }
}
