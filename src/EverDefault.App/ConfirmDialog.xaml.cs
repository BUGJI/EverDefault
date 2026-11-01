using System.Windows;

namespace EverDefault.App
{
    public partial class ConfirmDialog : DialogWindow
    {
        public ConfirmDialog()
        {
            InitializeComponent();
            WindowSizing.Clamp(this);
            Loaded += (sender, args) => CancelButton.Focus();
        }

        public ConfirmDialog(string message) : this()
        {
            MessageText.Text = message ?? string.Empty;
        }

        public void SetConfirmText(string text)
        {
            ConfirmButton.Content = text;
        }

        private void OnConfirm(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
        }

        private void OnCancel(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }
    }
}
