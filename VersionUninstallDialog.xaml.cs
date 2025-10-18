using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;

namespace ErenshorModInstaller.Wpf
{
    public partial class VersionUninstallDialog : Window
    {
        public sealed class StoredRow
        {
            public string Version { get; set; } = "0.0.0";
            public bool Remove { get; set; }
            public bool Keep { get; set; }
        }

        private readonly ObservableCollection<StoredRow> _rows = new();

        // With no separate “active” row, this remains false.
        public bool RemoveActive { get; private set; } = false;

        public IReadOnlyList<string> StoredVersionsToRemove { get; private set; } = new List<string>();
        public string? KeepAsActiveVersion { get; private set; }

        public VersionUninstallDialog(string displayName, string activeVersion, IEnumerable<string> storedVersions)
        {
            InitializeComponent();

            HeaderText.Text = $"Uninstall: {displayName}";
            ActiveInfo.Text = $"Active version: {activeVersion}";

            // Build rows from distinct stored versions; show each version only once.
            var distinctStored = new HashSet<string>(storedVersions ?? Enumerable.Empty<string>(),
                                                     System.StringComparer.OrdinalIgnoreCase);

            foreach (var v in distinctStored.OrderBy(v => v))
            {
                _rows.Add(new StoredRow
                {
                    Version = v,
                    Remove = false,
                    Keep = false
                });
            }

            // If the active version also exists in the store, pre-select its radio.
            var rowForActive = _rows.FirstOrDefault(r => string.Equals(r.Version, activeVersion, System.StringComparison.OrdinalIgnoreCase));
            if (rowForActive != null)
            {
                rowForActive.Keep = true;
            }

            StoredVersionsPanel.ItemsSource = _rows;
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            // Because all radios share GroupName="KeepGroup", at most ONE row can have Keep==true.
            var keepRow = _rows.FirstOrDefault(r => r.Keep);
            KeepAsActiveVersion = keepRow?.Version;

            // Never remove the kept version (if user checked Remove on same row by mistake, ignore it).
            var toRemove = _rows
                .Where(r => r.Remove && (keepRow == null || !string.Equals(r.Version, keepRow.Version, System.StringComparison.OrdinalIgnoreCase)))
                .Select(r => r.Version)
                .ToList();

            StoredVersionsToRemove = toRemove;

            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
