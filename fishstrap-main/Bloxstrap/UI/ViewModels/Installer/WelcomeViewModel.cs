namespace Bloxstrap.UI.ViewModels.Installer
{
    public class WelcomeViewModel : NotifyPropertyChangedViewModel
    {
        // formatting is done here instead of in xaml, it's just a bit easier
        public string MainText => String.Format(
            Strings.Installer_Welcome_MainText,
            "[github.com/jovanpogi1231-a11y/VananaStrap](https://github.com/jovanpogi1231-a11y/VananaStrap)"
        );

        public bool CanContinue { get; set; } = false;
    }
}
