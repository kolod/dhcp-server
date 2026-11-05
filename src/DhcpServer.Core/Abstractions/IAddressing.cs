using DhcpServer.Core.Addressing;

namespace DhcpServer.Core.Abstractions
{
    /// <summary>Reads an adapter's IPv4 configuration and performs one named change step on it.</summary>
    public interface IAdapterConfigService
    {
        /// <summary>The adapter's current configuration, or null when no such adapter exists.</summary>
        AdapterConfig Read(string adapterId);

        /// <summary>
        /// Performs one step on <c>target.AdapterId</c> and nothing else: address steps use the target's
        /// addresses (<paramref name="addressIndex"/> selects which one for <see cref="ApplyStep.AddAddress"/>),
        /// DNS steps use its DNS list. Verify, WaitForAddress and the record steps are not executed here.
        /// </summary>
        StepResult Execute(ApplyStep step, AdapterConfig target, int addressIndex = 0);
    }

    /// <summary>Persists the restore record. <c>Save</c> must throw on any failure and flush to disk.</summary>
    public interface IRestoreStore
    {
        /// <summary>The record, or null when absent. Throws <see cref="RestoreRecordCorruptException"/> when unreadable.</summary>
        SavedConfiguration Load();

        void Save(SavedConfiguration record);

        void Delete();
    }

    /// <summary>Only one instance may change or restore adapter settings at a time.</summary>
    public interface ISettingsLock
    {
        bool TryAcquire();

        void Release();
    }
}
