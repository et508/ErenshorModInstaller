using System.Windows;

namespace ErenshorModInstaller.Wpf.UI
{
    public enum PromptResult
    {
        None,
        Primary,
        Secondary,
        Destructive,
        Cancel
    }

    public partial class PromptDialog : Window
    {
        // Bindable props (kept simple; code-behind data context)
        public string TitleText { get; set; } = "Confirm";
        public string MessageText { get; set; } = "";
        public string DetailText { get; set; } = "";

        public string PrimaryText { get; set; } = "";
        public string SecondaryText { get; set; } = "";
        public string DestructiveText { get; set; } = "";
        public string CancelText { get; set; } = "Cancel";

        public Visibility PrimaryVisibility => string.IsNullOrWhiteSpace(PrimaryText) ? Visibility.Collapsed : Visibility.Visible;
        public Visibility SecondaryVisibility => string.IsNullOrWhiteSpace(SecondaryText) ? Visibility.Collapsed : Visibility.Visible;
        public Visibility DestructiveVisibility => string.IsNullOrWhiteSpace(DestructiveText) ? Visibility.Collapsed : Visibility.Visible;
        public Visibility CancelVisibility => string.IsNullOrWhiteSpace(CancelText) ? Visibility.Collapsed : Visibility.Visible;
        public Visibility DetailVisibility => string.IsNullOrWhiteSpace(DetailText) ? Visibility.Collapsed : Visibility.Visible;

        public bool PrimaryIsDefault { get; set; }
        public bool SecondaryIsDefault { get; set; }

        public PromptResult Result { get; private set; } = PromptResult.None;

        public PromptDialog()
        {
            InitializeComponent();
            DataContext = this;
        }

        // Fluent builder helpers
        public PromptDialog WithOwner(Window owner)
        {
            Owner = owner;
            return this;
        }
        
        public PromptDialog CenteredOnScreen()
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            return this;
        }

        public PromptDialog WithTitle(string title) { TitleText = title; return this; }
        public PromptDialog WithMessage(string message) { MessageText = message; return this; }
        public PromptDialog WithDetail(string detail) { DetailText = detail; return this; }

        public PromptDialog WithPrimary(string text, bool isDefault = false) { PrimaryText = text; PrimaryIsDefault = isDefault; return this; }
        public PromptDialog WithSecondary(string text, bool isDefault = false) { SecondaryText = text; SecondaryIsDefault = isDefault; return this; }
        public PromptDialog WithDestructive(string text) { DestructiveText = text; return this; }
        public PromptDialog WithCancel(string text = "Cancel") { CancelText = text; return this; }

        private void OnPrimary(object sender, RoutedEventArgs e)
        {
            Result = PromptResult.Primary;
            DialogResult = true;
            Close();
        }

        private void OnSecondary(object sender, RoutedEventArgs e)
        {
            Result = PromptResult.Secondary;
            DialogResult = true;
            Close();
        }

        private void OnDestructive(object sender, RoutedEventArgs e)
        {
            Result = PromptResult.Destructive;
            DialogResult = true;
            Close();
        }

        private void OnCancel(object sender, RoutedEventArgs e)
        {
            Result = PromptResult.Cancel;
            DialogResult = false; // keep false so Esc behaves like cancel
            Close();
        }
    }
}
