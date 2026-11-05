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

        /// <summary>The adapter can serve as it is (Principle I rules all pass).</summary>
        public bool IsEligible => _eligibility.IsEligible;

        /// <summary>Ethernet or Wi-Fi: the program may give it a fixed address.</summary>
        public bool IsConfigurable => InterfaceEligibility.IsConfigurable(_info);

        /// <summary>Connected, but its address is missing, automatic, too small or overlapping: a fixed address fixes that.</summary>
        public bool NeedsAddress => _info.IsUp && !IsEligible && IsConfigurable;

        /// <summary>Can be chosen: serves as it is, or can be configured.</summary>
        public bool IsSelectable => IsEligible || IsConfigurable;

        public string DisplayName
        {
            get
            {
                var address = _info.Address == null
                    ? LanguageService.Get("Interface_NoAddress")
                    : _info.Address.ToString();
                var status = LanguageService.Get(_info.IsUp ? "Interface_Status_Up" : "Interface_Status_Down");
                var text = _info.Name + " — " + address + " — " + status;
                return NeedsAddress ? text + " — " + LanguageService.Get("Interface_NeedsAddress") : text;
            }
        }

        /// <summary>Localized reason why the interface cannot serve as it is; null when it can.</summary>
        public string UnavailableReason
        {
            get
            {
                var reason = _eligibility.Reason;
                return reason == null ? null : FailureText.Format(reason);
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
