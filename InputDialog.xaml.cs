using System.Windows;

namespace ErenshorModInstaller.Wpf
{
    public partial class InputDialog : Window
    {
        public string DialogTitle { get; set; }
        public string PromptText { get; set; }
        public string ResponseText { get; set; }

        public InputDialog(string promptText, string title = "Input", string defaultValue = "")
        {
            InitializeComponent();
            
            DialogTitle = title;
            PromptText = promptText;
            ResponseText = defaultValue;
            
            DataContext = this;
            
            Loaded += (s, e) =>
            {
                ResponseTextBox.Focus();
                ResponseTextBox.SelectAll();
            };
        }

        private void OK_Click(object sender, RoutedEventArgs e)
        {
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