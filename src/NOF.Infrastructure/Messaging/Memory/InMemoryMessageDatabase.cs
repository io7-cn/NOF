using System.Collections.Concurrent;

namespace NOF.Infrastructure;

/// <summary>Host-local, tenant-partitioned message state for the development memory backend.</summary>
public sealed class InMemoryMessageDatabase
{
    private readonly ConcurrentDictionary<string, TenantMessages> _tenants = new(StringComparer.Ordinal);
    /// <summary>Gets one tenant's isolated state.</summary>
    internal TenantMessages ForTenant(string tenant) => _tenants.GetOrAdd(tenant, _ => new());
    /// <summary>Owns only message intents and receipts; business persistence belongs to application ports.</summary>
    public sealed class TenantMessages
    {
        internal object Gate { get; } = new();
        internal Dictionary<Guid, NOFOutboxMessage> Outbox { get; } = [];
        internal Dictionary<(Guid Id, string Route), NOFInboxMessage> Inbox { get; } = [];
    }
}
