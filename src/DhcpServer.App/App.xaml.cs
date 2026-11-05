using System.Windows;
using DhcpServer.App.Localization;
using DhcpServer.App.Settings;
using DhcpServer.App.ViewModels;
using DhcpServer.App.Views;

namespace DhcpServer.App
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            var settings = PortableSettings.Load();
            var language = settings.Language;
            LanguageService.SetLanguage(LanguageService.IsSupported(language) ? language : LanguageService.DetectOsLanguage());

            var window = new MainWindow { DataContext = new MainViewModel(settings) };
            MainWindow = window;
            window.Show();
        }
    }
}
