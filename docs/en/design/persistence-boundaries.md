# Aggregate repositories and native persistence

[简体中文](../../zh-CN/design/persistence-boundaries.md) · [Development](../README.md)

Domain owns aggregate-specific repository interfaces; Application owns DTO read ports and IUnitOfWork.
Those interfaces expose business operations and data, with no IQueryable, tracking switches, entity sets
or query language. Hosts implement them using their chosen native persistence APIs.

```csharp
public interface IConfigNodeRepository
{
    Task<ConfigNode?> GetAsync(ConfigNodeId id, CancellationToken ct = default);
    void Add(ConfigNode node);
    void Remove(ConfigNode node);
}

// Host implementation: native EF Core, sharing the scoped context with NOF stores.
public sealed class ConfigNodePersistence(DbContext context) : IConfigNodeRepository
{
    public Task<ConfigNode?> GetAsync(ConfigNodeId id, CancellationToken ct = default)
        => context.Set<ConfigNode>().FirstOrDefaultAsync(node => node.Id == id, ct);
    public void Add(ConfigNode node) => context.Set<ConfigNode>().Add(node);
    public void Remove(ConfigNode node) => context.Set<ConfigNode>().Remove(node);
}
```

The compiled sample adapter also implements the configuration read and hierarchy ports. Native EF
queries retain mapping expressions, owned graphs, uniqueness constraints, tenant filters and soft-delete
conventions. Repositories stage work; handlers save once through the shared IUnitOfWork. A precheck
never replaces a database uniqueness or concurrency constraint.

## Shared unit of work

IUnitOfWork.SaveChangesAsync returns Task, without provider-dependent counts. Optional
ITransactionManager.BeginTransactionAsync returns ITransaction with async commit, rollback and disposal.
Saving flushes inside an explicit transaction; committing makes those changes durable. Dispose or
rollback an incomplete transaction and discard the scope before retrying. An unknown commit outcome
requires idempotent recovery, not an assumption that rollback succeeded.

EF Core uses the same scoped DbContext for business adapters, EfCoreUnitOfWork, Inbox and Outbox.
NHibernate uses the same scoped ISession for those responsibilities. Hosts retain their native factory,
query, transaction, savepoint and mapping APIs. There is no second context for framework-owned writes.

## Native provider implementations

The cross-ORM collaboration layer is removed: IPersistenceSession/factory/transaction, IEntitySet,
IProviderQuery/executor, ProviderQuery, shared async query extensions, UpdateSetters, IDbModelBuilder
and IDbContextModelCreatingContributor no longer ship. Provider adapters no longer translate a NOF
query/update/model language. The shared general-purpose memory ORM and its reflection tracker are
also removed. No forwarding aliases or compatibility implementations remain.

Infrastructure keeps the feature-specific IInboxStore and IOutboxStore ports consumed by workers.
EF implements those ports with native EF queries and ExecuteUpdateAsync/ExecuteDeleteAsync.
NHibernate uses native LINQ DML; bounded claims select scalar candidate identities and atomically
compare-and-set each still-eligible row. Both backends preserve attempt limits, delayed Outbox retries,
lease recovery, tenant isolation and owner-fenced completion/failure/release.

Inbox completion shares the handler's transaction. Native bulk claims bypass ORM tracking, so a
receipt already cached by the processing session is refreshed before use. EF uses a claim-owner
concurrency token; NHibernate uses native dirty optimistic locking and dynamic updates. An expired
worker cannot overwrite a replacement owner's receipt when saving its result. Claim result projections
are delivery snapshots rather than cached NHibernate entities.

EF-specific model extensions implement IEFCoreModelConfiguration.Configure(ModelBuilder) in the
EF provider package, using native EF APIs. NHibernate extensions register Action<ModelMapper>
callbacks and configure native mapping-by-code. Value-object conversion and EF length/collection,
tenant and soft-delete conventions remain provider features. Application code injects neither model
configuration nor native sessions.

## OIDC persistence

The OIDC extension retains shared protocol/client-management/signing-key rules. Its storage boundary
is IOAuthClientStore, ISigningKeyStore and IRevokedRefreshTokenStore: feature operations and tracked
state, with no generic query or ORM model API.

Choose the matching optional integration package and register it on the host's services:

```csharp
// Package NOF.Hosting.AspNetCore.Extension.OidcServer.EntityFrameworkCore
builder.Services.AddOidcEntityFrameworkCore();
// Or package NOF.Hosting.AspNetCore.Extension.OidcServer.NHibernate
builder.Services.AddOidcNHibernate();
```

Configure the corresponding native persistence provider and AddOidcServer as usual. Native OIDC stores
share its scoped unit of work. The base OIDC package has no EF/NHibernate dependency; neither generic
provider package depends on the optional OIDC feature. Custom storage can implement the three narrow
ports and supply the same unit-of-work boundary.

## Memory messages and tests

AddNOF retains a development memory message backend. AddInMemoryMessagingPersistence registers
its host-local, tenant-partitioned state and scoped message unit of work; AddInbox/AddOutbox select
its stores unless a native provider or custom store is configured. Capabilities and workers remain
opt-in. Native and custom registrations work before or after capability assembly.

This backend stores only intents and receipts. It atomically saves receipt completion plus staged
messages, isolates tenants and fences claims. It has no arbitrary business entity tables or ORM API.
Business persistence tests should select an actual provider, for example UseDbContext with SQLite
in memory, or register business-owned test ports and an appropriate unit of work. NOF.Test does not
pretend that arbitrary domain objects have automatically become persistent.

## Migration

Replace native adapter dependencies on the removed collaboration interfaces with DbContext/ISession
inside infrastructure, leaving business-owned ports unchanged. Register native model configuration
and optional OIDC integration. Replace AddInMemoryPersistence/AddPersistenceServices with a selected
native provider; the message-only memory default remains available without either ORM package.

Inbox no longer stores a Command/Notification classification: identity is MessageId plus the opaque
Route, and execution resolves the route's keyed handler. MemoryMessageClient directly accepts queue
or subscription receipts; MemoryCommandRider/MemoryNotificationRider and InboxMessageType are removed.
The PostgreSQL sample migration 20261114170000_RemoveInboxClassification drops only that unused
column after the earlier migrations, preserving payloads, Headers, IDs, leases and deduplication history.
Stop old binaries before applying it. Down refuses to infer legacy classifications; restore a matching
backup and package set. Existing SQLite receiver files need an explicit equivalent schema migration;
EnsureCreated does not upgrade them.

The native query/model API change itself does not rename tables or reinterpret data. Review generated
provider migrations before deployment and migrate every tenant database deliberately.
