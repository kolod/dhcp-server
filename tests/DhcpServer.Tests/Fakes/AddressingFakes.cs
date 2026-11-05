using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using DhcpServer.Core.Abstractions;
using DhcpServer.Core.Addressing;

namespace DhcpServer.Tests.Fakes
{
    /// <summary>
    /// An in-memory adapter service. Every executed step is logged and its effect applied to the stored
    /// configuration, so tests can assert both order and final state; steps can be made to fail or to be
    /// silently ignored (so that verification fails).
    /// </summary>
    internal sealed class FakeAdapterConfigService : IAdapterConfigService
    {
        private readonly List<string> _log;
        private readonly Dictionary<ApplyStep, int> _failOn = new Dictionary<ApplyStep, int>();
        private readonly Dictionary<ApplyStep, int> _seen = new Dictionary<ApplyStep, int>();

        public FakeAdapterConfigService(List<string> log = null)
        {
            _log = log ?? new List<string>();
        }

        public Dictionary<string, AdapterConfig> Adapters { get; } = new Dictionary<string, AdapterConfig>();

        /// <summary>Every (step, adapter id) the service executed, in order.</summary>
        public List<KeyValuePair<ApplyStep, string>> Calls { get; } = new List<KeyValuePair<ApplyStep, string>>();

        public HashSet<ApplyStep> Ignored { get; } = new HashSet<ApplyStep>();

        /// <summary>Raised after a step changed an adapter (lets a fixture keep a fake interface provider in sync).</summary>
        public Action<AdapterConfig> Changed { get; set; }

        public static readonly IPAddress LeasedAddress = IPAddress.Parse("192.168.99.55");

        /// <summary>Makes the n-th execution of <paramref name="step"/> fail.</summary>
        public void FailOn(ApplyStep step, int occurrence = 1)
        {
            _failOn[step] = occurrence;
        }

        public void Set(AdapterConfig config)
        {
            Adapters[config.AdapterId] = config;
        }

        public AdapterConfig Read(string adapterId)
        {
            AdapterConfig config;
            return Adapters.TryGetValue(adapterId, out config) ? config : null;
        }

        public StepResult Execute(ApplyStep step, AdapterConfig target, int addressIndex = 0)
        {
            _log.Add("step:" + step);
            Calls.Add(new KeyValuePair<ApplyStep, string>(step, target.AdapterId));

            int count;
            _seen.TryGetValue(step, out count);
            _seen[step] = ++count;

            int failAt;
            if (_failOn.TryGetValue(step, out failAt) && failAt == count)
            {
                return StepResult.Fail(step, 1);
            }

            AdapterConfig current;
            if (!Adapters.TryGetValue(target.AdapterId, out current))
            {
                return StepResult.Fail(step, 1);
            }

            if (Ignored.Contains(step))
            {
                return StepResult.Ok(step);
            }

            Adapters[target.AdapterId] = Apply(step, current, target, addressIndex);
            Changed?.Invoke(Adapters[target.AdapterId]);
            return StepResult.Ok(step);
        }

        private static AdapterConfig Apply(ApplyStep step, AdapterConfig c, AdapterConfig t, int index)
        {
            switch (step)
            {
                case ApplyStep.SetAddress:
                    return new AdapterConfig(c.AdapterId, c.InterfaceIndex, new[] { t.Addresses[0] }, t.Gateway, c.DnsServers, AddressMode.Static, c.DnsMode);
                case ApplyStep.AddAddress:
                    return new AdapterConfig(c.AdapterId, c.InterfaceIndex, c.Addresses.Concat(new[] { t.Addresses[index] }), c.Gateway, c.DnsServers, c.AddressMode, c.DnsMode);
                case ApplyStep.SetDns:
                    return new AdapterConfig(c.AdapterId, c.InterfaceIndex, c.Addresses, c.Gateway, t.DnsServers, c.AddressMode, DnsMode.Static);
                case ApplyStep.ClearDns:
                    return new AdapterConfig(c.AdapterId, c.InterfaceIndex, c.Addresses, c.Gateway, new IPAddress[0], c.AddressMode, DnsMode.Static);
                case ApplyStep.SetAddressDhcp:
                    return new AdapterConfig(c.AdapterId, c.InterfaceIndex, new[] { new AdapterAddress(LeasedAddress, 24) }, null, c.DnsServers, AddressMode.Automatic, c.DnsMode);
                case ApplyStep.SetDnsDhcp:
                    return new AdapterConfig(c.AdapterId, c.InterfaceIndex, c.Addresses, c.Gateway, new IPAddress[0], c.AddressMode, DnsMode.Automatic);
                default:
                    return c;
            }
        }
    }

    internal sealed class InMemoryRestoreStore : IRestoreStore
    {
        private readonly List<string> _log;

        public InMemoryRestoreStore(List<string> log = null)
        {
            _log = log ?? new List<string>();
        }

        public SavedConfiguration Record { get; set; }

        public bool Corrupt { get; set; }

        public bool ThrowOnSave { get; set; }

        public bool ThrowOnDelete { get; set; }

        public int SaveCount { get; private set; }

        public SavedConfiguration Load()
        {
            if (Corrupt)
            {
                throw new RestoreRecordCorruptException("test");
            }

            return Record;
        }

        public void Save(SavedConfiguration record)
        {
            _log.Add("record:save");
            if (ThrowOnSave)
            {
                throw new System.IO.IOException("save");
            }

            SaveCount++;
            Record = record;
        }

        public void Delete()
        {
            _log.Add("record:delete");
            if (ThrowOnDelete)
            {
                throw new System.IO.IOException("delete");
            }

            Record = null;
            Corrupt = false;
        }
    }

    internal sealed class FakeSettingsLock : ISettingsLock
    {
        private readonly List<string> _log;

        public FakeSettingsLock(List<string> log = null)
        {
            _log = log ?? new List<string>();
        }

        public bool Refuse { get; set; }

        public bool Held { get; private set; }

        public bool TryAcquire()
        {
            if (Refuse || Held)
            {
                return false;
            }

            Held = true;
            _log.Add("lock");
            return true;
        }

        public void Release()
        {
            if (Held)
            {
                Held = false;
                _log.Add("unlock");
            }
        }
    }

    internal static class AddressingTestData
    {
        public const string AdapterId = "eth0";

        public static AdapterConfig Dhcp(string id = AdapterId)
        {
            return new AdapterConfig(id, 7, new[] { new AdapterAddress(IPAddress.Parse("10.20.30.40"), 24) }, IPAddress.Parse("10.20.30.1"),
                new IPAddress[0], AddressMode.Automatic, DnsMode.Automatic);
        }

        public static AdapterConfig Static(string id = AdapterId, string ip = "10.20.30.40", int prefix = 24, string gateway = "10.20.30.1", params string[] dns)
        {
            return new AdapterConfig(id, 7, new[] { new AdapterAddress(IPAddress.Parse(ip), prefix) },
                gateway == null ? null : IPAddress.Parse(gateway),
                dns.Select(IPAddress.Parse), AddressMode.Static, DnsMode.Static);
        }

        public static AdapterConfig StaticMulti(string id = AdapterId)
        {
            return new AdapterConfig(id, 7,
                new[] { new AdapterAddress(IPAddress.Parse("10.20.30.40"), 24), new AdapterAddress(IPAddress.Parse("172.16.5.5"), 16) },
                IPAddress.Parse("10.20.30.1"), new[] { IPAddress.Parse("8.8.8.8"), IPAddress.Parse("1.1.1.1") },
                AddressMode.Static, DnsMode.Static);
        }

        public static AddressSettings Settings(string ip = "192.168.137.1", int prefix = 24, string gateway = null, params string[] dns)
        {
            return new AddressSettings(IPAddress.Parse(ip), prefix, gateway == null ? null : IPAddress.Parse(gateway), dns.Select(IPAddress.Parse));
        }
    }
}
