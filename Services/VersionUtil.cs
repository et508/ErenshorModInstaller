using System;
using System.Text.RegularExpressions;

namespace ErenshorModInstaller.Wpf.Services
{
    /// <summary>
    /// Minimal, forgiving version parsing & comparison for BepInPlugin versions.
    /// Accepts forms like "1.2.3", "2.0", "13.0.4+hash", "v1.0.0-rc1".
    /// Missing/invalid -> 0.0.0.
    /// </summary>
    public static class VersionUtil
    {
        private static readonly Regex Parts = new(@"^\D*?(\d+)(?:\.(\d+))?(?:\.(\d+))?(?:\.(\d+))?", RegexOptions.Compiled);

        public static Version ParseOrZero(string? s)
        {
            if (string.IsNullOrWhiteSpace(s)) return new Version(0, 0, 0, 0);

            var m = Parts.Match(s.Trim());
            if (!m.Success) return new Version(0, 0, 0, 0);

            int GetPart(int group)
                => int.TryParse(m.Groups[group].Value, out var v) ? v : 0;

            return new Version(GetPart(1), GetPart(2), GetPart(3), GetPart(4));
        }

        /// <summary>
        /// Returns -1 if a&lt;b, 0 if equal, +1 if a&gt;b.
        /// </summary>
        public static int Compare(string? a, string? b)
        {
            var va = ParseOrZero(a);
            var vb = ParseOrZero(b);
            return va.CompareTo(vb);
        }
    }
}