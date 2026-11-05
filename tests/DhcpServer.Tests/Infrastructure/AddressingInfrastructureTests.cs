using System;
using System.IO;
using System.Linq;
using System.Net;
using DhcpServer.App.Infrastructure;
using DhcpServer.Core.Addressing;
using DhcpServer.Tests.Fakes;
using Xunit;

namespace DhcpServer.Tests.Infrastructure
{
    public class NetshCommandsTests
    {
        private static string[] Build(ApplyStep step, AdapterConfig target, int addressIndex = 0)
        {
            return NetshCommands.Build(step, target, addressIndex, 13).ToArray();
        }

        [Fact]
        public void SetAddress_WithGatewayAndWithout()
        {
            var withGateway = AdapterConfig.FromSettings("eth0", 13, AddressingTestData.Settings("192.168.137.1", 24, "192.168.137.254"));
            Assert.Equal(
                new[] { "interface ipv4 set address name=13 source=static address=192.168.137.1 mask=255.255.255.0 gateway=192.168.137.254" },
                Build(ApplyStep.SetAddress, withGateway));

            var without = AdapterConfig.FromSettings("eth0", 13, AddressingTestData.Settings("10.1.2.3", 16));
            Assert.Equal(
                new[] { "interface ipv4 set address name=13 source=static address=10.1.2.3 mask=255.255.0.0 gateway=none" },
                Build(ApplyStep.SetAddress, without));
        }

        [Fact]
        public void AddAddress_UsesTheRequestedIndex()
        {
            Assert.Equal(
                new[] { "interface ipv4 add address name=13 address=172.16.5.5 mask=255.255.0.0" },
                Build(ApplyStep.AddAddress, AddressingTestData.StaticMulti(), 1));
        }

        [Fact]
        public void SetDns_OneOrTwoServers_AndClear()
        {
            var two = AdapterConfig.FromSettings("eth0", 13, AddressingTestData.Settings("192.168.137.1", 24, null, "8.8.8.8", "1.1.1.1"));
            Assert.Equal(
                new[]
                {
                    "interface ipv4 set dnsservers name=13 source=static address=8.8.8.8 validate=no",
                    "interface ipv4 add dnsservers name=13 address=1.1.1.1 validate=no"
                },
                Build(ApplyStep.SetDns, two));
            Assert.Equal(
                new[] { "interface ipv4 set dnsservers name=13 source=static address=none validate=no" },
                Build(ApplyStep.ClearDns, two));
        }

        [Fact]
        public void DhcpSteps()
        {
            var dhcp = AddressingTestData.Dhcp();
            Assert.Equal(new[] { "interface ipv4 set address name=13 source=dhcp" }, Build(ApplyStep.SetAddressDhcp, dhcp));
            Assert.Equal(new[] { "interface ipv4 set dnsservers name=13 source=dhcp" }, Build(ApplyStep.SetDnsDhcp, dhcp));
        }

        [Fact]
        public void StepsWithoutACommand_BuildNothing()
        {
            var config = AddressingTestData.Static();
            foreach (var step in new[] { ApplyStep.Capture, ApplyStep.WriteRecord, ApplyStep.Verify, ApplyStep.WaitForAddress, ApplyStep.DeleteRecord })
            {
                Assert.Empty(Build(step, config));
            }
        }

        [Fact]
        public void CommandLines_ContainOnlyNumbersAddressesAndKeywords()
        {
            var config = AdapterConfig.FromSettings("eth0", 13, AddressingTestData.Settings("192.168.137.1", 24, "192.168.137.254", "8.8.8.8", "1.1.1.1"));
            foreach (var step in new[] { ApplyStep.SetAddress, ApplyStep.SetDns, ApplyStep.ClearDns, ApplyStep.SetAddressDhcp, ApplyStep.SetDnsDhcp })
            {
                foreach (var line in Build(step, config))
                {
                    Assert.Matches(@"^[a-z0-9 =.]+$", line);
                }
            }
        }
    }

    public sealed class FileRestoreStoreTests : IDisposable
    {
        private readonly string _dir = Path.Combine(Path.GetTempPath(), "DhcpServerRestore-" + Guid.NewGuid().ToString("N"));

        public FileRestoreStoreTests()
        {
            Directory.CreateDirectory(_dir);
        }

        private static SavedConfiguration Sample()
        {
            var previous = AddressingTestData.StaticMulti();
            return new SavedConfiguration("Ethernet 5", previous,
                AdapterConfig.FromSettings(previous.AdapterId, previous.InterfaceIndex, AddressingTestData.Settings()), DateTime.UtcNow);
        }

        [Fact]
        public void MissingFile_LoadsNull()
        {
            Assert.Null(new FileRestoreStore(Path.Combine(_dir, "none.restore")).Load());
        }

        [Fact]
        public void SaveLoadDelete_RoundTrip()
        {
            var path = Path.Combine(_dir, FileRestoreStore.FileName);
            var store = new FileRestoreStore(path);

            store.Save(Sample());
            store.Save(Sample().AsRestoring());

            var loaded = store.Load();
            Assert.True(loaded.Restoring);
            Assert.True(AdapterConfig.Equivalent(AddressingTestData.StaticMulti(), loaded.Previous));
            Assert.False(File.Exists(path + ".tmp"));

            store.Delete();
            Assert.False(File.Exists(path));
            Assert.Null(store.Load());
        }

        [Fact]
        public void CorruptFile_Throws()
        {
            var path = Path.Combine(_dir, "bad.restore");
            File.WriteAllText(path, "not a restore file");
            Assert.Throws<RestoreRecordCorruptException>(() => new FileRestoreStore(path).Load());
        }

        [Fact]
        public void UnwritableLocation_ThrowsFromSave_AndLeavesNoTempFile()
        {
            var store = new FileRestoreStore(Path.Combine(_dir, "missing-folder", "x.restore"));
            Assert.ThrowsAny<IOException>(() => store.Save(Sample()));
            Assert.Empty(Directory.GetFiles(_dir, "*", SearchOption.AllDirectories));
        }

        [Fact]
        public void ReadOnlyExistingFile_ThrowsFromSave()
        {
            var path = Path.Combine(_dir, "ro.restore");
            File.WriteAllText(path, "x");
            File.SetAttributes(path, FileAttributes.ReadOnly);
            try
            {
                Assert.ThrowsAny<Exception>(() => new FileRestoreStore(path).Save(Sample()));
            }
            finally
            {
                File.SetAttributes(path, FileAttributes.Normal);
            }
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(_dir, true);
            }
            catch (IOException)
            {
            }
        }
    }

    public class NamedSemaphoreSettingsLockTests
    {
        [Fact]
        public void SecondInstance_CannotAcquire_UntilReleased_EvenFromAnotherThread()
        {
            using (var first = new NamedSemaphoreSettingsLock())
            using (var second = new NamedSemaphoreSettingsLock())
            {
                Assert.True(first.TryAcquire());
                Assert.False(first.TryAcquire());
                Assert.False(second.TryAcquire());

                var thread = new System.Threading.Thread(first.Release);
                thread.Start();
                thread.Join();

                Assert.True(second.TryAcquire());
                second.Release();
            }
        }

        [Fact]
        public void Release_WithoutAcquire_IsHarmless()
        {
            using (var l = new NamedSemaphoreSettingsLock())
            {
                l.Release();
                Assert.True(l.TryAcquire());
            }
        }
    }

    public class AdapterConfigEmptyDnsTests
    {
        [Fact]
        public void EmptyStaticDns_EqualsAutomaticEmpty()
        {
            var staticEmpty = new AdapterConfig("eth0", 1, AddressingTestData.Static().Addresses, IPAddress.Parse("10.20.30.1"), new IPAddress[0], AddressMode.Static, DnsMode.Static);
            var automaticEmpty = new AdapterConfig("eth0", 1, AddressingTestData.Static().Addresses, IPAddress.Parse("10.20.30.1"), new IPAddress[0], AddressMode.Static, DnsMode.Automatic);
            Assert.True(AdapterConfig.Equivalent(staticEmpty, automaticEmpty));
        }

        [Fact]
        public void NonEmptyStaticDns_DiffersFromAutomatic()
        {
            var withDns = AddressingTestData.Static(dns: new[] { "8.8.8.8" });
            var automatic = new AdapterConfig("eth0", 1, withDns.Addresses, withDns.Gateway, new IPAddress[0], AddressMode.Static, DnsMode.Automatic);
            Assert.False(AdapterConfig.Equivalent(withDns, automatic));
        }
    }
}
