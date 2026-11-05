using System;
using System.IO;
using System.Linq;
using System.Net;
using DhcpServer.App.Infrastructure;
using DhcpServer.Core.Leases;
using DhcpServer.Tests.Fakes;
using Xunit;

namespace DhcpServer.Tests.Infrastructure
{
    public sealed class FileLeaseStoreTests : IDisposable
    {
        private readonly string _dir = Path.Combine(Path.GetTempPath(), "DhcpServerStore-" + Guid.NewGuid().ToString("N"));

        public FileLeaseStoreTests()
        {
            Directory.CreateDirectory(_dir);
        }

        private static Lease Sample(int mac, string ip)
        {
            var t = DateTime.UtcNow;
            return new Lease(TestPackets.MacKey(mac), IPAddress.Parse(ip), "h" + mac, t, t.AddHours(4), false, 24);
        }

        [Fact]
        public void MissingFile_LoadsEmpty()
        {
            Assert.Empty(new FileLeaseStore(Path.Combine(_dir, "none.leases")).Load());
        }

        [Fact]
        public void SaveThenLoad_RoundTrips_AndReplacesExistingFile()
        {
            var path = Path.Combine(_dir, FileLeaseStore.FileName);
            var store = new FileLeaseStore(path);

            store.Save(new[] { Sample(1, "192.168.137.2") });
            store.Save(new[] { Sample(1, "192.168.137.2"), Sample(2, "192.168.137.3") });

            Assert.Equal(2, store.Load().Count);
            Assert.False(File.Exists(path + ".tmp"));
            Assert.StartsWith(LeaseFile.Header, File.ReadAllText(path));
        }

        [Fact]
        public void CorruptFile_LoadsWhatItCan_AndNeverThrows()
        {
            var path = Path.Combine(_dir, "bad.leases");
            File.WriteAllText(path, "garbage\r\n\0\0\0");
            Assert.Empty(new FileLeaseStore(path).Load());
        }

        [Fact]
        public void UnwritableLocation_Throws_AndLeavesNoTempFile()
        {
            var store = new FileLeaseStore(Path.Combine(_dir, "missing-folder", "x.leases"));
            Assert.ThrowsAny<IOException>(() => store.Save(new[] { Sample(1, "192.168.137.2") }));
            Assert.Empty(Directory.GetFiles(_dir, "*", SearchOption.AllDirectories));
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
}
