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
        
        public bool RemoveActive { get; private set; } = false;

        public IReadOnlyList<string> StoredVersionsToRemove { get; private set; } = new List<string>();
        public string? KeepAsActiveVersion { get; private set; }

        public VersionUninstallDialog(string displayName, string activeVersion, IEnumerable<string> storedVersions)
        {
            InitializeComponent();

            HeaderText.Text = $"Uninstall: {displayName}";
            ActiveInfo.Text = $"Active version: {activeVersion}";
            
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
            
            var rowForActive = _rows.FirstOrDefault(r => string.Equals(r.Version, activeVersion, System.StringComparison.OrdinalIgnoreCase));
            if (rowForActive != null)
            {
                rowForActive.Keep = true;
            }

            StoredVersionsPanel.ItemsSource = _rows;
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            var keepRow = _rows.FirstOrDefault(r => r.Keep);
            KeepAsActiveVersion = keepRow?.Version;
            
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
