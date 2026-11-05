using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using DhcpServer.Core.Addressing;
using DhcpServer.Core.Network;
using DhcpServer.Tests.Fakes;
using Xunit;

namespace DhcpServer.Tests.Addressing
{
    internal sealed class ConfigFixture
    {
        public ConfigFixture(AdapterConfig initial = null, string targetAddress = "192.168.137.1")
        {
            Service = new FakeAdapterConfigService(Log);
            Store = new InMemoryRestoreStore(Log);
            Lock = new FakeSettingsLock(Log);
            Original = initial ?? AddressingTestData.Dhcp();
            Service.Set(Original);
            Interfaces.Interfaces.Add(TestNet.Eth("eth0", targetAddress));
            Configurator = new AddressConfigurator(Service, Store, Lock, Interfaces, Clock, span => Clock.Advance(span));
        }

        public List<string> Log { get; } = new List<string>();

        public FakeAdapterConfigService Service { get; }

        public InMemoryRestoreStore Store { get; }

        public FakeSettingsLock Lock { get; }

        public FakeInterfaceProvider Interfaces { get; } = new FakeInterfaceProvider();

        public FakeClock Clock { get; } = new FakeClock();

        public AddressConfigurator Configurator { get; }

        public AdapterConfig Original { get; }

        public InterfaceInfo Adapter => TestNet.Eth("eth0", ip: null);

        public ApplyResult Apply(AddressSettings settings = null)
        {
            return Configurator.Apply(Adapter, settings ?? AddressingTestData.Settings());
        }

        public AdapterConfig Current => Service.Read("eth0");
    }

    public class AddressConfiguratorApplyTests
    {
        [Fact]
        public void Apply_RunsStepsInOrder_AndKeepsTheLock()
        {
            var f = new ConfigFixture();
            var result = f.Apply();

            Assert.True(result.Succeeded);
            Assert.Equal(new[] { "lock", "record:save", "step:SetAddress", "step:ClearDns" }, f.Log.ToArray());
            Assert.True(f.Lock.Held);
            Assert.True(f.Configurator.HasAppliedSettings);
            Assert.True(AdapterConfig.Equivalent(f.Current, AdapterConfig.FromSettings("eth0", 7, AddressingTestData.Settings())));
        }

        [Fact]
        public void Apply_WithDnsAndGateway_UsesSetDns()
        {
            var f = new ConfigFixture();
            f.Apply(AddressingTestData.Settings("192.168.137.1", 24, "192.168.137.254", "8.8.8.8"));
            Assert.Equal(new[] { "lock", "record:save", "step:SetAddress", "step:SetDns" }, f.Log.ToArray());
            Assert.Equal("192.168.137.254", f.Current.Gateway.ToString());
        }

        [Fact]
        public void RecordIsOnDisk_BeforeTheFirstChange()
        {
            var f = new ConfigFixture();
            f.Apply();
            Assert.True(f.Log.IndexOf("record:save") < f.Log.FindIndex(l => l.StartsWith("step:", StringComparison.Ordinal)));
            Assert.True(AdapterConfig.Equivalent(f.Original, f.Store.Record.Previous));
            Assert.True(AdapterConfig.Equivalent(AdapterConfig.FromSettings("eth0", 7, AddressingTestData.Settings()), f.Store.Record.Applied));
        }

        [Fact]
        public void RecordNotWritable_ChangesNothing_AndReleasesTheLock()
        {
            var f = new ConfigFixture();
            f.Store.ThrowOnSave = true;
            var result = f.Apply();

            Assert.Equal(ApplyOutcome.RecordNotWritable, result.Outcome);
            Assert.Empty(f.Service.Calls);
            Assert.True(AdapterConfig.Equivalent(f.Original, f.Current));
            Assert.False(f.Lock.Held);
        }

        [Fact]
        public void LockRefused_ChangesNothing()
        {
            var f = new ConfigFixture();
            f.Lock.Refuse = true;
            var result = f.Apply();

            Assert.Equal(ApplyOutcome.SettingsLocked, result.Outcome);
            Assert.Empty(f.Service.Calls);
            Assert.Equal(0, f.Store.SaveCount);
        }

        [Fact]
        public void PendingRecord_RefusesToApply()
        {
            var f = new ConfigFixture();
            f.Store.Record = new SavedConfiguration("x", f.Original, f.Original, DateTime.UtcNow);
            Assert.Equal(ApplyOutcome.PendingRecord, f.Apply().Outcome);
            Assert.Empty(f.Service.Calls);
            Assert.False(f.Lock.Held);
        }

        [Fact]
        public void CorruptRecord_RefusesToApply()
        {
            var f = new ConfigFixture();
            f.Store.Corrupt = true;
            Assert.Equal(ApplyOutcome.RecordCorrupt, f.Apply().Outcome);
            Assert.Empty(f.Service.Calls);
        }

        [Fact]
        public void OnlyTheChosenAdapter_IsEverTouched()
        {
            var f = new ConfigFixture();
            var other = AddressingTestData.Static("eth1", "172.16.0.9", 16, "172.16.0.1", "9.9.9.9");
            f.Service.Set(other);

            f.Apply(AddressingTestData.Settings("192.168.137.1", 24, "192.168.137.254", "8.8.8.8"));
            f.Configurator.Restore();

            Assert.NotEmpty(f.Service.Calls);
            Assert.All(f.Service.Calls, c => Assert.Equal("eth0", c.Value));
            Assert.True(AdapterConfig.Equivalent(other, f.Service.Read("eth1")));
        }

        [Fact]
        public void CaptureFails_WhenAdapterMissing_OrUnsupported()
        {
            var missing = new ConfigFixture();
            missing.Service.Adapters.Clear();
            var r = missing.Apply();
            Assert.Equal(ApplyOutcome.Failed, r.Outcome);
            Assert.Equal(ApplyStep.Capture, r.FailedStep);
            Assert.Empty(missing.Service.Calls);

            var noAddress = new ConfigFixture(new AdapterConfig("eth0", 7, new AdapterAddress[0], null, new IPAddress[0], AddressMode.Static, DnsMode.Static));
            Assert.Equal(ApplyStep.Capture, noAddress.Apply().FailedStep);

            var many = Enumerable.Range(1, AddressConfigurator.MaxSavedAddresses + 1).Select(i => new AdapterAddress(IPAddress.Parse("10.0.0." + i), 24));
            var tooMany = new ConfigFixture(new AdapterConfig("eth0", 7, many, null, new IPAddress[0], AddressMode.Static, DnsMode.Static));
            Assert.Equal(ApplyStep.Capture, tooMany.Apply().FailedStep);
            Assert.Empty(tooMany.Service.Calls);
            Assert.Equal(0, tooMany.Store.SaveCount);
        }

        [Fact]
        public void WaitForAddress_PollsUntilPreferred()
        {
            var f = new ConfigFixture();
            f.Interfaces.Replace(new InterfaceInfo("eth0", "Ethernet eth0", InterfaceKind.Ethernet, true, IPAddress.Parse("192.168.137.1"), 24, null, null, false));
            var polls = 0;
            var configurator = new AddressConfigurator(f.Service, f.Store, f.Lock, f.Interfaces, f.Clock, span =>
            {
                f.Clock.Advance(span);
                if (++polls == 3)
                {
                    f.Interfaces.Replace(TestNet.Eth("eth0", "192.168.137.1"));
                }
            });

            Assert.True(configurator.Apply(f.Adapter, AddressingTestData.Settings()).Succeeded);
            Assert.Equal(3, polls);
        }

        [Fact]
        public void WaitForAddress_TimesOutAfterTenSeconds_AndRollsBack()
        {
            var f = new ConfigFixture();
            f.Interfaces.Replace(new InterfaceInfo("eth0", "Ethernet eth0", InterfaceKind.Ethernet, true, IPAddress.Parse("192.168.137.1"), 24, null, null, false));
            var start = f.Clock.UtcNow;
            var result = f.Apply();

            Assert.Equal(ApplyOutcome.AddressNotReady, result.Outcome);
            Assert.Equal(ApplyStep.WaitForAddress, result.FailedStep);
            Assert.True(f.Clock.UtcNow - start >= TimeSpan.FromSeconds(10));
            Assert.Equal(RestoreOutcome.Restored, result.Rollback.Outcome);
            Assert.True(AdapterConfig.Equivalent(f.Original, f.Current));
            Assert.Null(f.Store.Record);
        }

        [Fact]
        public void VerifyMismatch_RollsBack()
        {
            var f = new ConfigFixture();
            f.Service.Ignored.Add(ApplyStep.SetAddress);
            var result = f.Apply();

            Assert.Equal(ApplyStep.Verify, result.FailedStep);
            Assert.Equal(RestoreOutcome.Restored, result.Rollback.Outcome);
            Assert.True(AdapterConfig.Equivalent(f.Original, f.Current));
        }
    }

    public class AddressConfiguratorRestoreTests
    {
        public static IEnumerable<object[]> Previous()
        {
            yield return new object[] { AddressingTestData.Dhcp() };
            yield return new object[] { AddressingTestData.Static("eth0", "10.20.30.40", 24, null) };
            yield return new object[] { AddressingTestData.Static("eth0", "10.20.30.40", 24, "10.20.30.1") };
            yield return new object[] { AddressingTestData.Static("eth0", "10.20.30.40", 24, "10.20.30.1", "8.8.8.8", "1.1.1.1") };
            yield return new object[] { AddressingTestData.Static("eth0", "10.20.30.40", 24, null, "9.9.9.9") };
            yield return new object[] { AddressingTestData.StaticMulti() };
        }

        [Theory]
        [MemberData(nameof(Previous))]
        public void AfterApplyAndRestore_AdapterEqualsOriginal(AdapterConfig original)
        {
            var f = new ConfigFixture(original);
            Assert.True(f.Apply(AddressingTestData.Settings("192.168.137.1", 24, "192.168.137.254", "8.8.4.4")).Succeeded);
            Assert.False(AdapterConfig.Equivalent(original, f.Current));

            var restore = f.Configurator.Restore();

            Assert.Equal(RestoreOutcome.Restored, restore.Outcome);
            Assert.True(AdapterConfig.Equivalent(original, f.Current));
            Assert.Null(f.Store.Record);
            Assert.False(f.Lock.Held);
            Assert.False(f.Configurator.HasAppliedSettings);
        }

        [Fact]
        public void RestoreSteps_ForAutomatic()
        {
            var f = new ConfigFixture(AddressingTestData.Dhcp());
            f.Apply();
            f.Log.Clear();
            f.Configurator.Restore();
            Assert.Equal(new[] { "lock", "record:save", "step:SetAddressDhcp", "step:SetDnsDhcp", "record:delete", "unlock" }.Skip(1).ToArray(),
                f.Log.Where(l => l != "lock").ToArray());
        }

        [Fact]
        public void RestoreSteps_ForSeveralAddressesAndDns()
        {
            var f = new ConfigFixture(AddressingTestData.StaticMulti());
            f.Apply();
            f.Log.Clear();
            f.Configurator.Restore();
            var steps = f.Log.Where(l => l.StartsWith("step:", StringComparison.Ordinal)).ToArray();
            Assert.Equal(new[] { "step:SetAddress", "step:AddAddress", "step:SetDns" }, steps);
            Assert.Equal("record:delete", f.Log.Last(l => l.StartsWith("record:", StringComparison.Ordinal)));
        }

        [Fact]
        public void ChangedBySomeoneElse_IsLeftAlone_AndRecordDeleted()
        {
            var f = new ConfigFixture(AddressingTestData.Static());
            f.Apply();
            var theirs = AddressingTestData.Static("eth0", "10.99.99.9", 24, null);
            f.Service.Set(theirs);
            f.Service.Calls.Clear();

            var restore = f.Configurator.Restore();

            Assert.Equal(RestoreOutcome.LeftUnchanged, restore.Outcome);
            Assert.Empty(f.Service.Calls);
            Assert.True(AdapterConfig.Equivalent(theirs, f.Current));
            Assert.Null(f.Store.Record);
            Assert.False(f.Lock.Held);
        }

        [Fact]
        public void AdapterMissing_KeepsTheRecord()
        {
            var f = new ConfigFixture(AddressingTestData.Static());
            f.Apply();
            f.Service.Adapters.Clear();
            f.Service.Calls.Clear();

            var restore = f.Configurator.Restore();

            Assert.Equal(RestoreOutcome.AdapterMissing, restore.Outcome);
            Assert.False(restore.IsFinal);
            Assert.NotNull(f.Store.Record);
            Assert.Empty(f.Service.Calls);
            Assert.False(f.Lock.Held);
        }

        [Fact]
        public void RestoringMarker_FinishesAHalfDoneRestore()
        {
            var f = new ConfigFixture(AddressingTestData.Static());
            f.Apply();
            // an earlier restore got as far as the address, then the program died
            f.Service.Set(AddressingTestData.Static("eth0", "10.20.30.40", 24, "10.20.30.1"));
            f.Store.Record = f.Store.Record.AsRestoring();

            Assert.Equal(RestoreOutcome.Restored, f.Configurator.Restore().Outcome);
            Assert.True(AdapterConfig.Equivalent(f.Original, f.Current));
        }

        [Fact]
        public void RestoreTwice_SecondFindsNoRecord()
        {
            var f = new ConfigFixture(AddressingTestData.Static());
            f.Apply();
            Assert.Equal(RestoreOutcome.Restored, f.Configurator.Restore().Outcome);
            var after = f.Current;
            Assert.Equal(RestoreOutcome.NoRecord, f.Configurator.Restore().Outcome);
            Assert.True(AdapterConfig.Equivalent(after, f.Current));
        }

        [Fact]
        public void CorruptRecord_CannotBeRestored()
        {
            var f = new ConfigFixture();
            f.Store.Corrupt = true;
            var restore = f.Configurator.Restore();
            Assert.Equal(RestoreOutcome.RecordCorrupt, restore.Outcome);
            Assert.Empty(f.Service.Calls);
            Assert.False(f.Lock.Held);
        }

        [Fact]
        public void RestoreFromAnotherInstance_NeedsTheLock()
        {
            var f = new ConfigFixture(AddressingTestData.Static());
            f.Apply();
            var other = new AddressConfigurator(f.Service, f.Store, new FakeSettingsLock { Refuse = true }, f.Interfaces, f.Clock, s => { });
            f.Service.Calls.Clear();
            Assert.Equal(RestoreOutcome.SettingsLocked, other.Restore().Outcome);
            Assert.Empty(f.Service.Calls);
        }

        public static IEnumerable<object[]> RestoreFailures()
        {
            yield return new object[] { AddressingTestData.Static("eth0", "10.20.30.40", 24, null), ApplyStep.SetAddress, 2 };
            yield return new object[] { AddressingTestData.Static("eth0", "10.20.30.40", 24, null), ApplyStep.ClearDns, 2 };
            yield return new object[] { AddressingTestData.StaticMulti(), ApplyStep.SetAddress, 2 };
            yield return new object[] { AddressingTestData.StaticMulti(), ApplyStep.AddAddress, 1 };
            yield return new object[] { AddressingTestData.StaticMulti(), ApplyStep.SetDns, 1 };
            yield return new object[] { AddressingTestData.Dhcp(), ApplyStep.SetAddressDhcp, 1 };
            yield return new object[] { AddressingTestData.Dhcp(), ApplyStep.SetDnsDhcp, 1 };
        }

        [Theory]
        [MemberData(nameof(RestoreFailures))]
        public void FailureAtEachRestoreStep_NamesTheStep_AndKeepsTheRecord(AdapterConfig original, ApplyStep step, int occurrence)
        {
            var f = new ConfigFixture(original);
            Assert.True(f.Apply().Succeeded);
            f.Service.FailOn(step, occurrence);

            var restore = f.Configurator.Restore();

            Assert.Equal(RestoreOutcome.Failed, restore.Outcome);
            Assert.Equal(step, restore.FailedStep);
            Assert.NotNull(f.Store.Record);
            Assert.True(f.Store.Record.Restoring);
            Assert.False(f.Lock.Held);
            Assert.True(AdapterConfig.Equivalent(original, restore.Record.Previous));
        }

        [Fact]
        public void RestoreThatDoesNotVerify_FailsAtVerify()
        {
            var f = new ConfigFixture(AddressingTestData.Static());
            f.Apply();
            f.Service.Ignored.Add(ApplyStep.SetAddress);
            f.Service.Ignored.Add(ApplyStep.ClearDns);

            var restore = f.Configurator.Restore();

            Assert.Equal(ApplyStep.Verify, restore.FailedStep);
            Assert.NotNull(f.Store.Record);
        }

        [Fact]
        public void RecordThatCannotBeDeleted_FailsAtDeleteRecord()
        {
            var f = new ConfigFixture(AddressingTestData.Static());
            f.Apply();
            f.Store.ThrowOnDelete = true;
            var restore = f.Configurator.Restore();
            Assert.Equal(RestoreOutcome.Failed, restore.Outcome);
            Assert.Equal(ApplyStep.DeleteRecord, restore.FailedStep);
        }
    }

    public class AddressConfiguratorFailureTests
    {
        [Theory]
        [InlineData(ApplyStep.SetAddress)]
        [InlineData(ApplyStep.ClearDns)]
        public void ApplyFailure_RollsBack_NamesTheStep(ApplyStep step)
        {
            var f = new ConfigFixture(AddressingTestData.Static("eth0", "10.20.30.40", 24, "10.20.30.1", "8.8.8.8"));
            f.Service.FailOn(step);

            var result = f.Apply();

            Assert.Equal(ApplyOutcome.Failed, result.Outcome);
            Assert.Equal(step, result.FailedStep);
            Assert.Equal(RestoreOutcome.Restored, result.Rollback.Outcome);
            Assert.True(AdapterConfig.Equivalent(f.Original, f.Current));
            Assert.Null(f.Store.Record);
            Assert.False(f.Lock.Held);
        }

        [Fact]
        public void SetDnsFailure_RollsBack()
        {
            var f = new ConfigFixture(AddressingTestData.Static("eth0", "10.20.30.40", 24, "10.20.30.1", "8.8.8.8"));
            f.Service.FailOn(ApplyStep.SetDns);

            var result = f.Apply(AddressingTestData.Settings("192.168.137.1", 24, null, "1.1.1.1"));

            Assert.Equal(ApplyStep.SetDns, result.FailedStep);
            Assert.True(AdapterConfig.Equivalent(f.Original, f.Current));
        }

        [Fact]
        public void FailingRollback_KeepsTheRecord_AndReportsBoth()
        {
            var f = new ConfigFixture(AddressingTestData.Dhcp());
            f.Service.FailOn(ApplyStep.SetAddress);
            f.Service.FailOn(ApplyStep.SetAddressDhcp);

            var result = f.Apply();

            Assert.Equal(ApplyStep.SetAddress, result.FailedStep);
            Assert.Equal(RestoreOutcome.Failed, result.Rollback.Outcome);
            Assert.Equal(ApplyStep.SetAddressDhcp, result.Rollback.FailedStep);
            Assert.NotNull(f.Store.Record);
            Assert.False(f.Lock.Held);
        }

        [Fact]
        public void PartialApply_IsRolledBackEvenThoughAdapterDiffersFromTheTarget()
        {
            // SetAddress worked, SetDns failed: the adapter is neither original nor target.
            var f = new ConfigFixture(AddressingTestData.Static("eth0", "10.20.30.40", 24, "10.20.30.1", "8.8.8.8"));
            f.Service.FailOn(ApplyStep.SetDns);
            var result = f.Apply(AddressingTestData.Settings("192.168.137.1", 24, null, "1.1.1.1"));

            Assert.Equal(RestoreOutcome.Restored, result.Rollback.Outcome);
            Assert.True(AdapterConfig.Equivalent(f.Original, f.Current));
        }
    }
}
