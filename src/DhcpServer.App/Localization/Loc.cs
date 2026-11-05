using System.ComponentModel;

namespace DhcpServer.App.Localization
{
    /// <summary>
    /// Bindable view of the current string table. Raises a change of the indexer when the
    /// language changes, so every bound text updates at runtime. Used through
    /// <see cref="LocExtension"/> in XAML.
    /// </summary>
    public sealed class Loc : INotifyPropertyChanged
    {
        private Loc()
        {
            LanguageService.LanguageChanged += (s, e) =>
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
        }

        public event PropertyChangedEventHandler PropertyChanged;

        public static Loc Instance { get; } = new Loc();

        public string this[string key] => LanguageService.Get(key);
    }
}
