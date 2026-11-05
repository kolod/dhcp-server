using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using DhcpServer.Core.Abstractions;
using DhcpServer.Core.Dhcp;
using DhcpServer.Core.Leases;
using DhcpServer.Core.Network;
using DhcpServer.Core.Runtime;

namespace DhcpServer.Tests.Fakes
{
    internal sealed class FakeClock : IClock
    {
        public DateTime UtcNow { get; set; } = new DateTime(2026, 11, 5, 12, 0, 0, DateTimeKind.Utc);

        public void Advance(TimeSpan span)
        {
            UtcNow += span;
        }
    }

    internal sealed class FakeEndpoint : IDhcpEndpoint
    {
        private readonly BlockingCollection<byte[]> _input = new BlockingCollection<byte[]>();
        private readonly List<byte[]> _sent = new List<byte[]>();
        private readonly List<string> _log;

        public FakeEndpoint(IPAddress address, List<string> log)
        {
            Address = address;
            _log = log;
        }

        public IPAddress Address { get; }

        public bool IsClosed { get; private set; }

        public List<byte[]> Sent
        {
            get { lock (_sent) { return _sent.ToList(); } }
        }

        public void Enqueue(byte[] datagram)
        {
            if (IsClosed)
            {
                return;
            }

            try
            {
                _input.Add(datagram);
            }
            catch (InvalidOperationException)
            {
                // closed concurrently: nothing receives it, exactly like a closed socket
            }
        }

        public bool TryReceive(out byte[] data)
        {
            try
            {
                data = _input.Take();
                return true;
            }
            catch (InvalidOperationException)
            {
                data = null;
                return false;
            }
        }

        public void SendBroadcast(byte[] data)
        {
            if (IsClosed)
            {
                throw new ObjectDisposedException("endpoint");
            }

            lock (_sent)
            {
                _sent.Add(data);
            }
        }

        public void Close()
        {
            if (IsClosed)
            {
                return;
            }

            IsClosed = true;
            _log?.Add("close");
            _input.CompleteAdding();
        }

        public void Dispose()
        {
            Close();
        }

        public bool WaitForSent(int count, int timeoutMs = 3000)
        {
            var until = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            while (DateTime.UtcNow < until)
            {
                if (Sent.Count >= count)
                {
                    return true;
                }

                Thread.Sleep(5);
            }

            return Sent.Count >= count;
        }
    }

    internal sealed class FakeEndpointFactory : IEndpointFactory
    {
        public List<string> Log { get; } = new List<string>();

        public List<IPAddress> Bound { get; } = new List<IPAddress>();

        public FakeEndpoint Last { get; private set; }

        public FailureReason FailWith { get; set; }

        public IDhcpEndpoint Bind(IPAddress localAddress)
        {
            Log.Add("bind");
            if (FailWith != null)
            {
                throw new EndpointBindException(FailWith);
            }

            Bound.Add(localAddress);
            Last = new FakeEndpoint(localAddress, Log);
            return Last;
        }
    }

    internal sealed class FakeInterfaceProvider : IInterfaceProvider
    {
        public List<InterfaceInfo> Interfaces { get; } = new List<InterfaceInfo>();

        public event EventHandler Changed;

        public IReadOnlyList<InterfaceInfo> GetInterfaces()
        {
            return Interfaces.ToList();
        }

        public void RaiseChanged()
        {
            Changed?.Invoke(this, EventArgs.Empty);
        }

        public void Replace(InterfaceInfo info)
        {
            Interfaces.RemoveAll(i => i.Id == info.Id);
            Interfaces.Add(info);
        }
    }

    internal sealed class FakeAddressProber : IAddressProber
    {
        public HashSet<string> InUse { get; } = new HashSet<string>();

        public List<string> Asked { get; } = new List<string>();

        public bool IsInUse(IPAddress address, IPAddress viaInterface)
        {
            Asked.Add(address.ToString());
            return InUse.Contains(address.ToString());
        }
    }

    internal sealed class FakeDhcpServerProbe : IDhcpServerProbe
    {
        private readonly List<string> _log;

        public FakeDhcpServerProbe(List<string> log = null)
        {
            _log = log;
        }

        public ProbeResult Result { get; set; } = ProbeResult.NoServer;

        /// <summary>When set, the probe waits for it or for cancellation.</summary>
        public ManualResetEventSlim Gate { get; set; }

        public int Calls { get; private set; }

        public ProbeResult Probe(IPAddress viaInterface, CancellationToken cancellation)
        {
            Calls++;
            _log?.Add("probe");
            Gate?.Wait(cancellation);
            return Result;
        }
    }

    internal sealed class InMemoryLeaseStore : ILeaseStore
    {
        public List<Lease> Initial { get; } = new List<Lease>();

        public List<Lease> Saved { get; private set; }

        public int SaveCount { get; private set; }

        public bool ThrowOnSave { get; set; }

        public bool ThrowOnLoad { get; set; }

        public IReadOnlyList<Lease> Load()
        {
            if (ThrowOnLoad)
            {
                throw new System.IO.IOException("load");
            }

            return Initial.ToList();
        }

        public void Save(IReadOnlyList<Lease> leases)
        {
            if (ThrowOnSave)
            {
                throw new System.IO.IOException("save");
            }

            SaveCount++;
            Saved = leases.ToList();
        }
    }

    internal static class TestNet
    {
        public static readonly IPAddress ServerIp = IPAddress.Parse("192.168.137.1");

        public static InterfaceInfo Eth(string id = "eth0", string ip = "192.168.137.1", int prefix = 24, bool up = true, string gateway = null)
        {
            return new InterfaceInfo(
                id, "Ethernet " + id, InterfaceKind.Ethernet, up,
                ip == null ? null : IPAddress.Parse(ip), prefix,
                gateway == null ? null : IPAddress.Parse(gateway),
                new byte[] { 2, 0, 0, 0, 0, 1 });
        }

        public static AddressPool Pool(string server = "192.168.137.1", int prefix = 24, string gateway = null)
        {
            return new AddressPool(IPAddress.Parse(server), prefix, gateway == null ? null : IPAddress.Parse(gateway));
        }
    }

    /// <summary>Builders for client datagrams.</summary>
    internal static class TestPackets
    {
        public static byte[] Mac(int n)
        {
            return new byte[] { 0xAA, 0xBB, 0xCC, 0x00, (byte)(n >> 8), (byte)n };
        }

        public static string MacKey(int n)
        {
            return MacAddress.ToKey(Mac(n));
        }

        public static DhcpMessage Base(int mac, DhcpMessageType type, uint xid = 1)
        {
            var m = new DhcpMessage { Op = 1, Xid = xid, ClientMac = Mac(mac) };
            m.SetByteOption(DhcpOptionCode.MessageType, (byte)type);
            return m;
        }

        public static byte[] Discover(int mac, string requested = null, string host = null)
        {
            var m = Base(mac, DhcpMessageType.Discover);
            if (requested != null)
            {
                m.SetAddressOption(DhcpOptionCode.RequestedAddress, IPAddress.Parse(requested));
            }

            if (host != null)
            {
                m.Options[DhcpOptionCode.HostName] = System.Text.Encoding.ASCII.GetBytes(host);
            }

            return m.ToBytes();
        }

        public static byte[] RequestSelecting(int mac, string address, string serverId = "192.168.137.1", string host = null)
        {
            var m = Base(mac, DhcpMessageType.Request);
            m.SetAddressOption(DhcpOptionCode.RequestedAddress, IPAddress.Parse(address));
            m.SetAddressOption(DhcpOptionCode.ServerIdentifier, IPAddress.Parse(serverId));
            if (host != null)
            {
                m.Options[DhcpOptionCode.HostName] = System.Text.Encoding.ASCII.GetBytes(host);
            }

            return m.ToBytes();
        }

        public static byte[] RequestInitReboot(int mac, string address)
        {
            var m = Base(mac, DhcpMessageType.Request);
            m.SetAddressOption(DhcpOptionCode.RequestedAddress, IPAddress.Parse(address));
            return m.ToBytes();
        }

        public static byte[] RequestRenew(int mac, string ciaddr)
        {
            var m = Base(mac, DhcpMessageType.Request);
            m.CiAddr = IPAddress.Parse(ciaddr);
            return m.ToBytes();
        }

        public static byte[] Release(int mac, string ciaddr)
        {
            var m = Base(mac, DhcpMessageType.Release);
            m.CiAddr = IPAddress.Parse(ciaddr);
            return m.ToBytes();
        }

        public static byte[] Decline(int mac, string address, string serverId = "192.168.137.1")
        {
            var m = Base(mac, DhcpMessageType.Decline);
            m.SetAddressOption(DhcpOptionCode.RequestedAddress, IPAddress.Parse(address));
            m.SetAddressOption(DhcpOptionCode.ServerIdentifier, IPAddress.Parse(serverId));
            return m.ToBytes();
        }

        public static byte[] Inform(int mac, string ciaddr)
        {
            var m = Base(mac, DhcpMessageType.Inform);
            m.CiAddr = IPAddress.Parse(ciaddr);
            return m.ToBytes();
        }

        public static DhcpMessage Parse(byte[] reply)
        {
            DhcpMessage m;
            Xunit.Assert.True(DhcpMessage.TryParse(reply, out m));
            return m;
        }
    }
}

namespace DhcpServer.Tests.Fakes
{
    internal static class TaskExtensions
    {
        /// <summary>Waits for a task from a non-test helper (xUnit analyzers forbid blocking in test methods).</summary>
        public static void Join(this System.Threading.Tasks.Task task, int timeoutMs = 10000)
        {
            task.Wait(timeoutMs);
        }
    }
}
