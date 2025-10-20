using System;
using System.Text.RegularExpressions;

namespace ErenshorModInstaller.Wpf.Services
{
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
        
        public static int Compare(string? a, string? b)
        {
            var va = ParseOrZero(a);
            var vb = ParseOrZero(b);
            return va.CompareTo(vb);
        }
    }
}