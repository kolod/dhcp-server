using System;
using System.Collections.Generic;
using System.Linq;
using DhcpServer.App.Localization;
using DhcpServer.Core.Abstractions;
using DhcpServer.Core.Addressing;
using DhcpServer.Core.Network;

namespace DhcpServer.App.ViewModels
{
    /// <summary>
    /// The "set a fixed address for this session" group: fields, suggestion, live validation. All rules
    /// live in Core (<see cref="AddressSettingsValidator"/>, <see cref="AddressSuggester"/>).
    /// </summary>
    public sealed class AddressSettingsViewModel : ObservableObject
    {
        private readonly Func<IReadOnlyList<InterfaceInfo>> _interfaces;
        private readonly IAddressProber _prober;

        private InterfaceItemViewModel _adapter;
        private bool _useFixed;
        private bool _touched;
        private string _address = string.Empty;
        private string _mask = string.Empty;
        private string _gateway = string.Empty;
        private string _dns1 = string.Empty;
        private string _dns2 = string.Empty;
        private IReadOnlyList<FieldError> _errors = new FieldError[0];

        internal AddressSettingsViewModel(Func<IReadOnlyList<InterfaceInfo>> interfaces, IAddressProber prober = null)
        {
            _interfaces = interfaces;
            _prober = prober;
        }

        /// <summary>True when the adapter cannot serve as it is, so a fixed address is required.</summary>
        public bool IsLocked => _adapter != null && _adapter.NeedsAddress;

        public bool CanToggle => _adapter != null && !IsLocked;

        public bool FieldsReadOnly => !_useFixed;

        public bool UseFixedAddress
        {
            get { return _useFixed; }
            set
            {
                if (_adapter == null || (IsLocked && !value))
                {
                    OnPropertyChanged();
                    return;
                }

                if (!SetProperty(ref _useFixed, value))
                {
                    return;
                }

                if (value && !_touched)
                {
                    FillSuggestion();
                }
                else if (!value)
                {
                    _touched = false;
                    ShowCurrent();
                }

                Revalidate();
                RaiseAll();
            }
        }

        public string Address
        {
            get { return _address; }
            set { Edit(ref _address, value); }
        }

        public string Mask
        {
            get { return _mask; }
            set { Edit(ref _mask, value); }
        }

        public string Gateway
        {
            get { return _gateway; }
            set { Edit(ref _gateway, value); }
        }

        public string Dns1
        {
            get { return _dns1; }
            set { Edit(ref _dns1, value); }
        }

        public string Dns2
        {
            get { return _dns2; }
            set { Edit(ref _dns2, value); }
        }

        public string AddressError => ErrorFor(AddressField.Address);

        public string MaskError => ErrorFor(AddressField.Mask);

        public string GatewayError => ErrorFor(AddressField.Gateway);

        public string Dns1Error => ErrorFor(AddressField.Dns1);

        public string Dns2Error => ErrorFor(AddressField.Dns2);

        public bool HasErrors => _useFixed && _errors.Count > 0;

        /// <summary>The first problem, with a warning glyph (the state is never shown by colour alone).</summary>
        public string FirstError
        {
            get
            {
                var first = _errors.FirstOrDefault();
                return !_useFixed || first == null
                    ? string.Empty
                    : "⚠ " + FieldName(first.Field) + ": " + LanguageService.Get(first.ResourceKey);
            }
        }

        /// <summary>True when Start may go ahead as far as addressing is concerned.</summary>
        public bool IsReadyToStart
        {
            get
            {
                if (_adapter == null)
                {
                    return false;
                }

                return _useFixed ? _errors.Count == 0 : _adapter.IsEligible;
            }
        }

        /// <summary>Selects the adapter the group is about; resets suggestion and "touched" state.</summary>
        public void SetAdapter(InterfaceItemViewModel adapter)
        {
            _adapter = adapter;
            _touched = false;
            _errors = new FieldError[0];

            if (adapter == null)
            {
                _useFixed = false;
                SetFields(string.Empty, string.Empty, string.Empty, string.Empty, string.Empty);
            }
            else if (adapter.NeedsAddress)
            {
                _useFixed = true;
                FillSuggestion();
            }
            else
            {
                _useFixed = false;
                ShowCurrent();
            }

            Revalidate();
            RaiseAll();
        }

        /// <summary>Called when the interface list or the language changed: re-evaluates lock state and messages.</summary>
        public void Refresh()
        {
            if (_adapter != null)
            {
                if (_adapter.NeedsAddress && !_useFixed)
                {
                    _useFixed = true;
                    if (!_touched)
                    {
                        FillSuggestion();
                    }
                }
                else if (!_useFixed && !_touched)
                {
                    ShowCurrent();
                }
            }

            Revalidate();
            RaiseAll();
        }

        /// <summary>
        /// Without a fixed address: true with <paramref name="settings"/> = null (nothing to change).
        /// With one: true only when all fields are valid.
        /// </summary>
        public bool TryGetSettings(out AddressSettings settings)
        {
            settings = null;
            if (!_useFixed)
            {
                return true;
            }

            var result = Validate();
            settings = result.Settings;
            return result.IsValid;
        }

        private void Edit(ref string field, string value, [System.Runtime.CompilerServices.CallerMemberName] string name = null)
        {
            value = value ?? string.Empty;
            if (field == value)
            {
                return;
            }

            field = value;
            _touched = true;
            OnPropertyChanged(name);
            Revalidate();
            RaiseAll();
        }

        private void FillSuggestion()
        {
            var suggestion = AddressSuggester.Suggest(_adapter.Id, _interfaces(), _prober);
            if (suggestion == null)
            {
                SetFields(string.Empty, string.Empty, string.Empty, string.Empty, string.Empty);
                return;
            }

            SetFields(suggestion.Address.ToString(), IpMath.MaskAddress(suggestion.PrefixLength).ToString(), string.Empty, string.Empty, string.Empty);
        }

        private void ShowCurrent()
        {
            var info = _adapter?.Info;
            if (info == null || info.Address == null)
            {
                SetFields(string.Empty, string.Empty, string.Empty, string.Empty, string.Empty);
                return;
            }

            SetFields(
                info.Address.ToString(),
                info.PrefixLength > 0 ? IpMath.MaskAddress(info.PrefixLength).ToString() : string.Empty,
                info.Gateway == null ? string.Empty : info.Gateway.ToString(),
                string.Empty,
                string.Empty);
        }

        private void SetFields(string address, string mask, string gateway, string dns1, string dns2)
        {
            _address = address;
            _mask = mask;
            _gateway = gateway;
            _dns1 = dns1;
            _dns2 = dns2;
        }

        private ValidationResult Validate()
        {
            return AddressSettingsValidator.Validate(
                new AddressFieldInput { Address = _address, Mask = _mask, Gateway = _gateway, Dns1 = _dns1, Dns2 = _dns2 },
                _adapter.Id,
                _interfaces());
        }

        private void Revalidate()
        {
            _errors = _useFixed && _adapter != null ? Validate().Errors : new FieldError[0];
        }

        private string ErrorFor(AddressField field)
        {
            var error = _useFixed ? _errors.FirstOrDefault(e => e.Field == field) : null;
            return error == null ? string.Empty : LanguageService.Get(error.ResourceKey);
        }

        private static string FieldName(AddressField field)
        {
            switch (field)
            {
                case AddressField.Address:
                    return LanguageService.Get("Address_Address");
                case AddressField.Mask:
                    return LanguageService.Get("Address_Mask");
                case AddressField.Gateway:
                    return LanguageService.Get("Address_Gateway");
                case AddressField.Dns1:
                    return LanguageService.Get("Address_Dns1");
                default:
                    return LanguageService.Get("Address_Dns2");
            }
        }

        private void RaiseAll()
        {
            OnPropertyChanged(null);
        }
    }
}
