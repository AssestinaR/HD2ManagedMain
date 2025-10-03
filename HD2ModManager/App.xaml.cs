using System.Globalization;
using System.Windows;

namespace HD2ModManager
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            ApplyLocalization();
            base.OnStartup(e);
        }

        private static void ApplyLocalization()
        {
            var lang = HD2ModManager.Services.SettingsService.GetLanguage() ?? System.Globalization.CultureInfo.CurrentUICulture.IetfLanguageTag;
            var culture = new CultureInfo(lang);
            HD2ModManager.Resources.Strings.Culture = culture;
            FrameworkElement.LanguageProperty.OverrideMetadata(
                typeof(FrameworkElement),
                new FrameworkPropertyMetadata(
                    System.Windows.Markup.XmlLanguage.GetLanguage(culture.IetfLanguageTag)));
        }
    }
}
