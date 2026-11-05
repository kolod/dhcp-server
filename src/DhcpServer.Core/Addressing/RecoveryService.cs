using DhcpServer.Core.Abstractions;

namespace DhcpServer.Core.Addressing
{
    public enum RecoveryState
    {
        NotRun,
        NothingToDo,
        Done,
        Pending,
        Corrupt
    }

    /// <summary>
    /// Start-up recovery: an unfinished session (record present) is restored before any new session may
    /// start. If that fails the user chooses Retry or Discard (contracts/apply-sequence.md).
    /// </summary>
    public sealed class RecoveryService
    {
        private readonly AddressConfigurator _configurator;
        private readonly IRestoreStore _store;

        public RecoveryService(AddressConfigurator configurator, IRestoreStore store)
        {
            _configurator = configurator;
            _store = store;
        }

        public RecoveryState State { get; private set; }

        /// <summary>The outcome of the last attempt; null before the first run.</summary>
        public RestoreResult LastResult { get; private set; }

        /// <summary>The unfinished record, for the banner (null when absent or corrupt).</summary>
        public SavedConfiguration PendingRecord { get; private set; }

        public bool CanStartSession => State == RecoveryState.NotRun || State == RecoveryState.NothingToDo || State == RecoveryState.Done;

        public RecoveryState Run()
        {
            SavedConfiguration record;
            try
            {
                record = _store.Load();
            }
            catch (RestoreRecordCorruptException)
            {
                PendingRecord = null;
                LastResult = new RestoreResult(RestoreOutcome.RecordCorrupt);
                return State = RecoveryState.Corrupt;
            }
            catch (System.Exception)
            {
                PendingRecord = null;
                LastResult = new RestoreResult(RestoreOutcome.Failed, ApplyStep.Capture);
                return State = RecoveryState.Pending;
            }

            if (record == null)
            {
                PendingRecord = null;
                return State = RecoveryState.NothingToDo;
            }

            PendingRecord = record;
            LastResult = _configurator.Restore();
            switch (LastResult.Outcome)
            {
                case RestoreOutcome.Restored:
                case RestoreOutcome.LeftUnchanged:
                case RestoreOutcome.NoRecord:
                    PendingRecord = null;
                    return State = RecoveryState.Done;
                case RestoreOutcome.RecordCorrupt:
                    PendingRecord = null;
                    return State = RecoveryState.Corrupt;
                default:
                    return State = RecoveryState.Pending;
            }
        }

        public RecoveryState Retry()
        {
            return Run();
        }

        /// <summary>The user knowingly gives up restoring: the record is deleted.</summary>
        public RecoveryState Discard()
        {
            try
            {
                _store.Delete();
            }
            catch (System.Exception)
            {
                return State;
            }

            PendingRecord = null;
            return State = RecoveryState.Done;
        }
    }
}
