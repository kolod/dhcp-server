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
using DhcpServer.Core.Addressing;
using DhcpServer.Core.Runtime;

namespace DhcpServer.App
{
    public partial class App : Application
    {
        private ServerSession _session;
        private MainViewModel _viewModel;
        private NamedSemaphoreSettingsLock _settingsLock;

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            var settings = PortableSettings.Load();
            var language = settings.Language;
            LanguageService.SetLanguage(LanguageService.IsSupported(language) ? language : LanguageService.DetectOsLanguage());

            var clock = new SystemClock();
            var interfaces = new NetworkInterfaceProvider();
            var adapters = new NetshAdapterConfigService();
            var restoreStore = FileRestoreStore.NextToExecutable();
            _settingsLock = new NamedSemaphoreSettingsLock();
            var configurator = new AddressConfigurator(adapters, restoreStore, _settingsLock, interfaces, clock);
            var recovery = new RecoveryService(configurator, restoreStore);

            _session = new ServerSession(
                new UdpEndpointFactory(),
                clock,
                interfaces,
                new DhcpServerProbe(),
                new ArpIcmpProber(),
                FileLeaseStore.NextToExecutable(),
                TimeSpan.FromSeconds(2),
                configurator,
                recovery);
            _viewModel = new MainViewModel(
                settings, _session, interfaces, clock, SynchronizationContext.Current, SetClipboard,
                adapters, new WpfUserDialogs(), recovery);

            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            timer.Tick += (s, args) => _viewModel.Tick();
            timer.Start();

            var window = new MainWindow { DataContext = _viewModel };
            MainWindow = window;
            window.Show();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            // Nothing may keep answering after the window is gone, and the PC's settings must be back (Principles I, II).
            _viewModel?.Dispose();
            _session?.Dispose();
            _settingsLock?.Dispose();
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
