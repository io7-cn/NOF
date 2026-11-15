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

Native persistence retains IOutboxStore and shared application IUnitOfWork/transaction integration. There is no Inbox store, claim, receipt or consumer model. Message endpoints require no database; business handlers/filters choose saves, transactions and atomic idempotency constraints. Native EF/NHibernate Outbox operations preserve bounded claims, tenant isolation, delays, lease recovery and owner fencing. Resolve old Inbox work and review the guarded table-removal migration before deployment; see [Outbox and migration](transactional-outbox.md).

## Native provider implementations

The cross-ORM collaboration layer is removed: IPersistenceSession/factory/transaction, IEntitySet,
IProviderQuery/executor, ProviderQuery, shared async query extensions, UpdateSetters, IDbModelBuilder
and IDbContextModelCreatingContributor no longer ship. Provider adapters no longer translate a NOF
query/update/model language. The shared general-purpose memory ORM and its reflection tracker are
also removed. No forwarding aliases or compatibility implementations remain.

EF-specific model extensions implement IEFCoreModelConfiguration.Configure(ModelBuilder) in the
EF provider package, using native EF APIs. NHibernate extensions register Action<ModelMapper>
callbacks and configure native mapping-by-code. Value-object conversion and EF length/collection,
tenant and soft-delete conventions remain provider features. Application code injects neither model
configuration nor native sessions.

## Identity persistence

The OIDC protocol and store integration packages are removed. Hosts use upstream providers directly. The Sample adds OpenIddict models to its existing ConfigurationDbContext and migration chain, disabling business soft deletion for those entities. Its new migration preserves legacy identity tables for an explicitly reviewed transition. See [host composition and identity migration](host-composition.md).

## Memory messages and tests

AddOutbox selects the development memory Outbox backend if no native/custom store was chosen. AddInMemoryMessagingPersistence registers
its host-local, tenant-partitioned state and scoped message unit of work; AddOutbox selects
its stores unless a native provider or custom store is configured. Capabilities and workers remain
opt-in. Native and custom registrations work before or after capability assembly.

This backend stores only outgoing intents. It atomically commits staged messages, isolates tenants and fences Outbox claims. It has no arbitrary business entity tables or ORM API.
Business persistence tests should select an actual provider, for example UseDbContext with SQLite
in memory, or register business-owned test ports and an appropriate unit of work. NOF.Test does not
pretend that arbitrary domain objects have automatically become persistent.

## Migration

Replace native adapter dependencies on the removed collaboration interfaces with DbContext/ISession
inside infrastructure, leaving business-owned ports unchanged. Register native model configuration
and the host's native identity integration. Replace AddInMemoryPersistence/AddPersistenceServices with a selected
native provider; the message-only memory default remains available without either ORM package.

The native query/model API change itself does not rename tables or reinterpret data. Review generated
provider migrations before deployment and migrate every tenant database deliberately.

## Persistence errors

Providers translate native optimistic save conflicts to System.Data.DBConcurrencyException and data-save failures to System.Data.DataException, preserving the original cause. Application has no database exception hierarchy. Transaction failures, cancellation and unexpected errors propagate. Native consumer implementations own transaction and idempotency decisions, not generic receipt reconciliation.
