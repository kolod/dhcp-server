using System;
using System.Windows.Input;
using DhcpServer.App.Localization;
using DhcpServer.Core.Leases;

namespace DhcpServer.App.ViewModels
{
    /// <summary>One row of the lease grid. Updated in place so the grid keeps its selection.</summary>
    public sealed class LeaseItemViewModel : ObservableObject
    {
        private readonly Action<string> _copy;
        private Lease _lease;
        private LeaseState _state;
        private bool _serverRunning;

        internal LeaseItemViewModel(Lease lease, DateTime now, bool serverRunning, Action<string> copy)
        {
            _copy = copy;
            CopyIpCommand = new RelayCommand(p => _copy(IpAddress));
            CopyMacCommand = new RelayCommand(p => _copy(MacAddressText));
            CopyRowCommand = new RelayCommand(p => _copy(IpAddress + "\t" + MacAddressText));
            Update(lease, now, serverRunning);
        }

        public ICommand CopyIpCommand { get; }

        public ICommand CopyMacCommand { get; }

        public ICommand CopyRowCommand { get; }

        /// <summary>Canonical MAC key (12 hex digits); identifies the row.</summary>
        public string Key => _lease.Mac;

        public string IpAddress => _lease.Address.ToString();

        public string MacAddressText => MacAddress.Format(_lease.Mac);

        public string HostName => _lease.HostName;

        public string StartText => _lease.Start.ToLocalTime().ToString("g", LanguageService.Culture);

        public string ExpiresText => _lease.Expires.ToLocalTime().ToString("g", LanguageService.Culture);

        /// <summary>Everything is "inactive" while the server is not running (the list is from an earlier session).</summary>
        public string StateText
        {
            get
            {
                if (!_serverRunning)
                {
                    return LanguageService.Get("LeaseState_Inactive");
                }

                switch (_state)
                {
                    case LeaseState.Active:
                        return LanguageService.Get("LeaseState_Active");
                    case LeaseState.Expired:
                        return LanguageService.Get("LeaseState_Expired");
                    default:
                        return LanguageService.Get("LeaseState_Released");
                }
            }
        }

        public bool IsInactive => !_serverRunning || _state != LeaseState.Active;

        public void Update(Lease lease, DateTime now, bool serverRunning)
        {
            _lease = lease;
            _state = lease.GetState(now);
            _serverRunning = serverRunning;
            OnPropertyChanged(null);
        }

        public void RefreshText()
        {
            OnPropertyChanged(null);
        }
    }
}
