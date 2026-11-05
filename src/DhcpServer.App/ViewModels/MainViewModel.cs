using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Windows.Input;
using DhcpServer.App.Localization;
using DhcpServer.App.Settings;

namespace DhcpServer.App.ViewModels
{
    public sealed class MainViewModel : ObservableObject
    {
        private readonly PortableSettings _settings;

        internal MainViewModel(PortableSettings settings)
        {
            _settings = settings;
            Languages = LanguageService.Supported
                .Select(code => new LanguageItem(code, LanguageService.NativeName(code)))
                .ToList();
            UpdateSelection();
            SelectLanguageCommand = new RelayCommand(p => SelectLanguage(p as string));
        }

        public IReadOnlyList<LanguageItem> Languages { get; }

        public ICommand SelectLanguageCommand { get; }

        public string Version
        {
            get
            {
                var info = typeof(MainViewModel).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
                var version = info?.InformationalVersion ?? "0.0.0";
                var plus = version.IndexOf('+');
                return plus >= 0 ? version.Substring(0, plus) : version;
            }
        }

        private void SelectLanguage(string code)
        {
            if (string.IsNullOrEmpty(code))
            {
                return;
            }

            LanguageService.SetLanguage(code);
            UpdateSelection();
            _settings.Language = LanguageService.Current;
            _settings.TrySave();
        }

        private void UpdateSelection()
        {
            foreach (var item in Languages)
            {
                item.IsSelected = item.Code == LanguageService.Current;
            }
        }
    }
}
