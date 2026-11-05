using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using DhcpServer.App.Infrastructure;
using DhcpServer.App.Localization;
using DhcpServer.App.Settings;
using DhcpServer.App.ViewModels;
using DhcpServer.App.Views;
using DhcpServer.Core.Runtime;

namespace DhcpServer.App
{
    public partial class App : Application
    {
        private ServerSession _session;
        private MainViewModel _viewModel;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            var settings = PortableSettings.Load();
            var language = settings.Language;
            LanguageService.SetLanguage(LanguageService.IsSupported(language) ? language : LanguageService.DetectOsLanguage());

            var clock = new SystemClock();
            var interfaces = new NetworkInterfaceProvider();
            _session = new ServerSession(
                new UdpEndpointFactory(),
                clock,
                interfaces,
                new DhcpServerProbe(),
                new ArpIcmpProber(),
                FileLeaseStore.NextToExecutable(),
                TimeSpan.FromSeconds(2));
            _viewModel = new MainViewModel(settings, _session, interfaces, clock, SynchronizationContext.Current, SetClipboard);

            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            timer.Tick += (s, args) => _viewModel.Tick();
            timer.Start();

            var window = new MainWindow { DataContext = _viewModel };
            MainWindow = window;
            window.Show();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            // Nothing may keep answering after the window is gone (Principle I).
            _viewModel?.Dispose();
            _session?.Dispose();
            base.OnExit(e);
        }

        private static void SetClipboard(string text)
        {
            try
            {
                Clipboard.SetText(text);
            }
            catch (COMException)
            {
                // Another program holds the clipboard; copying again will work.
            }
        }
    }
}
