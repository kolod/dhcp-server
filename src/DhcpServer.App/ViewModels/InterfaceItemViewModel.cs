using DhcpServer.App.Localization;
using DhcpServer.Core.Network;

namespace DhcpServer.App.ViewModels
{
    /// <summary>One row of the interface selector: name, IPv4 address, status and (if unusable) why.</summary>
    public sealed class InterfaceItemViewModel : ObservableObject
    {
        private InterfaceInfo _info;
        private Eligibility _eligibility;

        public InterfaceItemViewModel(InterfaceInfo info, Eligibility eligibility)
        {
            _info = info;
            _eligibility = eligibility;
        }

        public InterfaceInfo Info => _info;

        public string Id => _info.Id;

        public bool IsSelectable => _eligibility.IsEligible;

        public string DisplayName
        {
            get
            {
                var address = _info.Address == null
                    ? LanguageService.Get("Interface_NoAddress")
                    : _info.Address.ToString();
                var status = LanguageService.Get(_info.IsUp ? "Interface_Status_Up" : "Interface_Status_Down");
                return _info.Name + " — " + address + " — " + status;
            }
        }

        /// <summary>Localized reason why the interface cannot be used; null when it can.</summary>
        public string UnavailableReason
        {
            get
            {
                var reason = _eligibility.Reason;
                return reason == null ? null : LanguageService.Format(reason.ResourceKey, reason.Argument ?? string.Empty);
            }
        }

        public void Update(InterfaceInfo info, Eligibility eligibility)
        {
            _info = info;
            _eligibility = eligibility;
            RefreshText();
        }

        /// <summary>Re-reads all texts (after a runtime language switch or an update).</summary>
        public void RefreshText()
        {
            OnPropertyChanged(null);
        }
    }
}
