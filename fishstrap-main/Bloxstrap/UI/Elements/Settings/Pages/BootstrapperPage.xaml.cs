using System.Windows;

using Bloxstrap.UI.ViewModels.Settings;

namespace Bloxstrap.UI.Elements.Settings.Pages
{
    /// <summary>
    /// Interaction logic for BootstrapperPage.xaml
    /// </summary>
    public partial class BootstrapperPage
    {
        public BootstrapperPage()
        {
            DataContext = new BehaviourViewModel();
            InitializeComponent();
        }

        private void ToggleSwitch_Checked(object sender, RoutedEventArgs e)
        {

        }
    }
}
