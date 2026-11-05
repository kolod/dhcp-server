namespace DhcpServer.App.ViewModels
{
    public sealed class LanguageItem : ObservableObject
    {
        private bool _isSelected;

        public LanguageItem(string code, string nativeName)
        {
            Code = code;
            NativeName = nativeName;
        }

        public string Code { get; }

        public string NativeName { get; }

        public bool IsSelected
        {
            get { return _isSelected; }
            set { SetProperty(ref _isSelected, value); }
        }
    }
}
