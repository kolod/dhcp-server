using System;

namespace DhcpServer.Core.Addressing
{
    /// <summary>The persisted record: what the adapter had before, and what we set (see contracts/restore-file.md).</summary>
    public sealed class SavedConfiguration
    {
        public SavedConfiguration(string adapterName, AdapterConfig previous, AdapterConfig applied, DateTime createdUtc, bool restoring = false)
        {
            AdapterName = adapterName ?? string.Empty;
            Previous = previous;
            Applied = applied;
            CreatedUtc = createdUtc;
            Restoring = restoring;
        }

        public string AdapterName { get; }

        public AdapterConfig Previous { get; }

        public AdapterConfig Applied { get; }

        public DateTime CreatedUtc { get; }

        /// <summary>Set before the first restore step; a half-finished restore can then be completed later.</summary>
        public bool Restoring { get; }

        public string AdapterId => Previous.AdapterId;

        public SavedConfiguration AsRestoring()
        {
            return new SavedConfiguration(AdapterName, Previous, Applied, CreatedUtc, true);
        }
    }

    public sealed class RestoreRecordCorruptException : Exception
    {
        public RestoreRecordCorruptException(string message)
            : base(message)
        {
        }
    }
}
