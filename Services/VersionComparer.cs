using System;

namespace ErenshorModInstaller.Wpf.Services
{
    internal static class VersionComparer
    {
        public static int Compare(string a, string b)
        {
            var asv = Normalize(a).Split(new[] { '.', '-', '+' }, StringSplitOptions.RemoveEmptyEntries);
            var bsv = Normalize(b).Split(new[] { '.', '-', '+' }, StringSplitOptions.RemoveEmptyEntries);

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

                int cmp = string.Compare(pa, pb, StringComparison.OrdinalIgnoreCase);
                if (cmp != 0) return cmp;
            }
            return 0;
        }

        private static string Normalize(string version)
        {
            return string.IsNullOrWhiteSpace(version) ? "0.0.0" : version.Trim();
        }
    }
}
