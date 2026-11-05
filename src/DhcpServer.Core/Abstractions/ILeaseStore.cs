using System.Collections.Generic;
using DhcpServer.Core.Leases;

namespace DhcpServer.Core.Abstractions
{
    /// <summary>Persists leases. Implementations may throw IO exceptions; callers treat that as non-fatal.</summary>
    public interface ILeaseStore
    {
        IReadOnlyList<Lease> Load();

        void Save(IReadOnlyList<Lease> leases);
    }
}
