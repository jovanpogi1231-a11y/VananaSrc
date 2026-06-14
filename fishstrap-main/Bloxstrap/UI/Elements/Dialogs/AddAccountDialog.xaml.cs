using System.Windows;

namespace Bloxstrap.UI.Elements.Dialogs
{
    public partial class AddAccountDialog
    {
        public string Cookie { get; private set; } = string.Empty;

        public AddAccountDialog()
        {
            InitializeComponent();
        }

        private void OKButton_Click(object sender, RoutedEventArgs e)
        {
            Cookie = CookieTextBox.Text.Trim();
            DialogResult = true;
            Close();
        }
    }
}