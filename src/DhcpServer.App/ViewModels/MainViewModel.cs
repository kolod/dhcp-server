using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Input;
using DhcpServer.App.Localization;
using DhcpServer.App.Settings;
using DhcpServer.Core.Abstractions;
using DhcpServer.Core.Network;
using DhcpServer.Core.Runtime;

namespace DhcpServer.App.ViewModels
{
    public sealed class MainViewModel : ObservableObject, IDisposable
    {
        private static readonly TimeSpan FirewallHintDelay = TimeSpan.FromSeconds(60);

        private readonly PortableSettings _settings;
        private readonly ServerSession _session;
        private readonly IInterfaceProvider _interfaces;
        private readonly IClock _clock;
        private readonly SynchronizationContext _ui;
        private readonly Action<string> _setClipboard;

        private InterfaceItemViewModel _selectedInterface;
        private LeaseItemViewModel _selectedLease;
        private bool _poolExhausted;
        private bool _storeFailed;
        private bool _firewallHint;
        private DateTime _runningSince;

        internal MainViewModel(
            PortableSettings settings,
            ServerSession session,
            IInterfaceProvider interfaces,
            IClock clock,
            SynchronizationContext ui,
            Action<string> setClipboard)
        {
            _settings = settings;
            _session = session;
            _interfaces = interfaces;
            _clock = clock;
            _ui = ui;
            _setClipboard = setClipboard;
            _storeFailed = session.StoreFailedOnLoad;

            Languages = LanguageService.Supported
                .Select(code => new LanguageItem(code, LanguageService.NativeName(code)))
                .ToList();
            UpdateLanguageSelection();
            SelectLanguageCommand = new RelayCommand(p => SelectLanguage(p as string));
            StartStopCommand = new RelayCommand(p => StartStop(), p => CanStartStop);
            CopyRowCommand = new RelayCommand(p => SelectedLease?.CopyRowCommand.Execute(null), p => SelectedLease != null);

            RefreshInterfaces();
            var last = _settings.LastInterface;
            if (!string.IsNullOrEmpty(last))
            {
                var remembered = Interfaces.FirstOrDefault(i => i.Id == last && i.IsSelectable);
                if (remembered != null)
                {
                    _selectedInterface = remembered;
                }
            }

            RefreshLeases();

            _session.StateChanged += OnSessionStateChanged;
            _session.PoolExhausted += OnPoolExhausted;
            _session.StoreFailed += OnStoreFailed;
            _session.Leases.Changed += OnLeasesChanged;
            _interfaces.Changed += OnInterfacesChanged;
            LanguageService.LanguageChanged += OnLanguageChanged;
        }

        public IReadOnlyList<LanguageItem> Languages { get; }

        public ICommand SelectLanguageCommand { get; }

        public ICommand StartStopCommand { get; }

        public ICommand CopyRowCommand { get; }

        public ObservableCollection<InterfaceItemViewModel> Interfaces { get; } = new ObservableCollection<InterfaceItemViewModel>();

        public ObservableCollection<LeaseItemViewModel> Leases { get; } = new ObservableCollection<LeaseItemViewModel>();

        public InterfaceItemViewModel SelectedInterface
        {
            get { return _selectedInterface; }
            set
            {
                if (value != null && !value.IsSelectable)
                {
                    return;
                }

                if (SetProperty(ref _selectedInterface, value))
                {
                    RaiseStatus();
                }
            }
        }

        public LeaseItemViewModel SelectedLease
        {
            get { return _selectedLease; }
            set { SetProperty(ref _selectedLease, value); }
        }

        public bool HasLeases => Leases.Count > 0;

        public ServerState State => _session.State;

        /// <summary>The interface cannot be changed while the server runs or starts.</summary>
        public bool CanChangeInterface => State == ServerState.Stopped || State == ServerState.Failed;

        public bool CanStartStop => State == ServerState.Starting || State == ServerState.Running
            || (SelectedInterface != null && SelectedInterface.IsSelectable);

        public string StartStopText
        {
            get
            {
                switch (State)
                {
                    case ServerState.Starting:
                        return LanguageService.Get("Button_Cancel");
                    case ServerState.Running:
                        return LanguageService.Get("Button_Stop");
                    default:
                        return LanguageService.Get("Button_Start");
                }
            }
        }

        public string StateGlyph
        {
            get
            {
                switch (State)
                {
                    case ServerState.Running:
                        return "●";
                    case ServerState.Starting:
                        return "◔";
                    case ServerState.Failed:
                        return "✖";
                    default:
                        return "○";
                }
            }
        }

        public string StatusText
        {
            get
            {
                switch (State)
                {
                    case ServerState.Starting:
                        return LanguageService.Get("Status_Starting");
                    case ServerState.Running:
                        var address = _session.Chosen?.Address;
                        return LanguageService.Format("Status_Running", address == null ? string.Empty : address.ToString());
                    case ServerState.Failed:
                        return LanguageService.Get("Status_Failed");
                    default:
                        return LanguageService.Get("Status_Stopped");
                }
            }
        }

        /// <summary>Why the server did not start or stopped, or a hint about the next step.</summary>
        public string DetailText
        {
            get
            {
                var failure = _session.Failure;
                if (failure != null)
                {
                    return LanguageService.Format(failure.ResourceKey, failure.Argument ?? string.Empty);
                }

                if (State == ServerState.Stopped && SelectedInterface == null)
                {
                    return LanguageService.Get("Hint_ChooseInterface");
                }

                return string.Empty;
            }
        }

        /// <summary>Non-blocking notices: pool exhausted, lease file not writable, firewall hint.</summary>
        public string NoticeText
        {
            get
            {
                var lines = new List<string>();
                if (_poolExhausted)
                {
                    lines.Add(LanguageService.Get("Notice_PoolExhausted"));
                }

                if (_storeFailed)
                {
                    lines.Add(LanguageService.Get("Notice_LeaseFileNotWritable"));
                }

                if (_firewallHint)
                {
                    lines.Add(LanguageService.Get("Notice_FirewallHint"));
                }

                return string.Join(Environment.NewLine, lines);
            }
        }

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

        /// <summary>Called about once a second by the UI timer: ages the lease states and shows the firewall hint.</summary>
        public void Tick()
        {
            if (State == ServerState.Running && !_firewallHint && _session.PacketsReceived == 0
                && _clock.UtcNow - _runningSince >= FirewallHintDelay)
            {
                _firewallHint = true;
                OnPropertyChanged(nameof(NoticeText));
            }

            RefreshLeases();
        }

        public void Dispose()
        {
            _session.StateChanged -= OnSessionStateChanged;
            _session.PoolExhausted -= OnPoolExhausted;
            _session.StoreFailed -= OnStoreFailed;
            _session.Leases.Changed -= OnLeasesChanged;
            _interfaces.Changed -= OnInterfacesChanged;
            LanguageService.LanguageChanged -= OnLanguageChanged;
        }

        private void StartStop()
        {
            switch (_session.State)
            {
                case ServerState.Starting:
                    _session.CancelStart();
                    break;
                case ServerState.Running:
                    _session.Stop();
                    break;
                default:
                    var chosen = SelectedInterface;
                    if (chosen == null || !chosen.IsSelectable)
                    {
                        return;
                    }

                    _settings.LastInterface = chosen.Id;
                    _settings.TrySave();
                    _poolExhausted = false;
                    _firewallHint = false;
                    _session.StartAsync(chosen.Info);
                    break;
            }
        }

        private void RefreshInterfaces()
        {
            var all = _interfaces.GetInterfaces();
            var visible = all.Where(i => i.Kind != InterfaceKind.Loopback).OrderBy(i => i.Name, StringComparer.CurrentCultureIgnoreCase).ToList();

            foreach (var stale in Interfaces.Where(item => visible.All(v => v.Id != item.Id)).ToList())
            {
                Interfaces.Remove(stale);
                if (ReferenceEquals(_selectedInterface, stale))
                {
                    SelectedInterface = null;
                }
            }

            for (var index = 0; index < visible.Count; index++)
            {
                var info = visible[index];
                var eligibility = InterfaceEligibility.Evaluate(info, all);
                var existing = Interfaces.FirstOrDefault(i => i.Id == info.Id);
                if (existing == null)
                {
                    Interfaces.Insert(Math.Min(index, Interfaces.Count), new InterfaceItemViewModel(info, eligibility));
                }
                else
                {
                    existing.Update(info, eligibility);
                }
            }

            // A selected interface that became unusable cannot be started any more.
            if (_selectedInterface != null && !_selectedInterface.IsSelectable && CanChangeInterface)
            {
                SelectedInterface = null;
            }

            RaiseStatus();
        }

        private void RefreshLeases()
        {
            var now = _clock.UtcNow;
            var running = _session.State == ServerState.Running;
            var snapshot = _session.Leases.Snapshot();
            var byKey = Leases.ToDictionary(l => l.Key);

            foreach (var gone in Leases.Where(l => snapshot.All(s => s.Mac != l.Key)).ToList())
            {
                Leases.Remove(gone);
                byKey.Remove(gone.Key);
            }

            for (var index = 0; index < snapshot.Count; index++)
            {
                var lease = snapshot[index];
                LeaseItemViewModel item;
                if (byKey.TryGetValue(lease.Mac, out item))
                {
                    item.Update(lease, now, running);
                    var current = Leases.IndexOf(item);
                    if (current != index)
                    {
                        Leases.Move(current, index);
                    }
                }
                else
                {
                    item = new LeaseItemViewModel(lease, now, running, CopyToClipboard);
                    Leases.Insert(index, item);
                    byKey[lease.Mac] = item;
                }
            }

            OnPropertyChanged(nameof(HasLeases));
        }

        private void CopyToClipboard(string text)
        {
            _setClipboard?.Invoke(text);
        }

        private void RaiseStatus()
        {
            OnPropertyChanged(nameof(State));
            OnPropertyChanged(nameof(CanChangeInterface));
            OnPropertyChanged(nameof(CanStartStop));
            OnPropertyChanged(nameof(StartStopText));
            OnPropertyChanged(nameof(StateGlyph));
            OnPropertyChanged(nameof(StatusText));
            OnPropertyChanged(nameof(DetailText));
            OnPropertyChanged(nameof(NoticeText));
            CommandManager.InvalidateRequerySuggested();
        }

        private void OnSessionStateChanged(object sender, EventArgs e)
        {
            Post(() =>
            {
                if (_session.State == ServerState.Running)
                {
                    _runningSince = _clock.UtcNow;
                }

                RefreshInterfaces();
                RefreshLeases();
            });
        }

        private void OnPoolExhausted(object sender, EventArgs e)
        {
            Post(() =>
            {
                _poolExhausted = true;
                OnPropertyChanged(nameof(NoticeText));
            });
        }

        private void OnStoreFailed(object sender, EventArgs e)
        {
            Post(() =>
            {
                _storeFailed = true;
                OnPropertyChanged(nameof(NoticeText));
            });
        }

        private void OnLeasesChanged(object sender, EventArgs e)
        {
            Post(RefreshLeases);
        }

        private void OnInterfacesChanged(object sender, EventArgs e)
        {
            Post(RefreshInterfaces);
        }

        private void OnLanguageChanged(object sender, EventArgs e)
        {
            Post(() =>
            {
                foreach (var item in Interfaces)
                {
                    item.RefreshText();
                }

                foreach (var lease in Leases)
                {
                    lease.RefreshText();
                }

                RaiseStatus();
            });
        }

        private void Post(Action action)
        {
            if (_ui == null)
            {
                action();
            }
            else
            {
                _ui.Post(_ => action(), null);
            }
        }

        private void SelectLanguage(string code)
        {
            if (string.IsNullOrEmpty(code))
            {
                return;
            }

            LanguageService.SetLanguage(code);
            UpdateLanguageSelection();
            _settings.Language = LanguageService.Current;
            _settings.TrySave();
        }

        private void UpdateLanguageSelection()
        {
            foreach (var item in Languages)
            {
                item.IsSelected = item.Code == LanguageService.Current;
            }
        }
    }
}
