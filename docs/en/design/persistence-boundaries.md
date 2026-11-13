# Aggregate repositories and persistence boundaries

[简体中文](../../zh-CN/design/persistence-boundaries.md) · [Development](../README.md)

Domain/Application no longer publish a generic ORM facade. Application-owned read ports
return DTOs; domain-owned repositories load complete aggregates for modification. Application
coordinates one IUnitOfWork.SaveChangesAsync(CancellationToken), returning Task rather than
provider-dependent change counts. No Set<T>, IQueryable repository, ICollection repository,
Attach/Update, tracking switch, query executor or update-expression DSL is exposed by core.

```csharp
public interface IConfigNodeRepository
{
    Task<ConfigNode?> GetAsync(ConfigNodeId id, CancellationToken ct = default);
    Task<bool> ExistsByNameAsync(ConfigNodeName name, CancellationToken ct = default);
    void Add(ConfigNode node);
    void Remove(ConfigNode node);
}

// Application use case: dependencies are owned ports, not a generic context.
var node = await nodes.GetAsync(id, cancellationToken);
if (node is null)
{
    return new NotFound("Node not found.");
}
node.UpdateParent(parentId);
await notifications.DeferPublishAsync(message, context, cancellationToken);
await unitOfWork.SaveChangesAsync(cancellationToken);
```

GetAsync loads a managed aggregate including its owned children. Add/Remove stage work;
repositories do not save independently. Aggregate methods control child mutations. DTO
query ports own their materialization and return no provider query objects. Read adapters
may use native LINQ/SQL and mapping expressions internally. Unique constraints and optimistic
concurrency still belong in the persistence implementation; a precheck alone cannot prevent races.
No generic specification language or framework aggregate marker is required.

## Configuration-node implementation

Domain declares IConfigNodeRepository. Application declares IConfigNodeQueries and
IConfigNodeHierarchyStore, the latter maintaining the hierarchy projection during domain
events without saving it independently. Every CRUD/file/hierarchy/configuration handler now
uses these ports. ConfigNodePersistence in the host implements them, sharing the authorized,
scoped provider session. DTO reads return detached business data and copy mutable hierarchy
collections. Existing generated mappings, validation, cache keys and HTTP outcomes remain.
Register its assembly with AddApplicationPart; AutoInject supplies scoped implementations.
The sample integration harness links the same adapter and tests both memory and relational paths.

## Transactions and reliable messaging

ITransactionManager.BeginTransactionAsync returns the minimal ITransaction: asynchronous
CommitAsync, RollbackAsync and disposal. Without an explicit transaction, saving uses the
provider's atomic save. With one, SaveChangesAsync flushes inside it; CommitAsync makes the
transaction durable. Multiple saves do not commit the outer transaction. Savepoints and
native transaction identifiers are native provider concerns, absent from the shared contract.

A scoped PersistenceUnitOfWork delegates to the same session used by aggregate adapters,
projection stores, deferred outbox staging and inbox processing. Do not introduce a second
context for work that must be atomic. Outbox staging does not send immediately. On failure,
rollback or unknown commit outcome, discard the scope and recover with a fresh context and
an application idempotency key. A commit exception does not prove rollback.

Infrastructure defines IInboxStore/IOutboxStore for receiving, deduplication checks, claims,
ordering, recovery, completion, failures, retention and staging. Workers and memory/RabbitMQ
riders consume these ports. Provider-backed stores retain the existing atomic transitions;
application handler writes and inbox completion share the same transaction. Retries, lease
expiry, ordering and cancellation remain covered by the existing delivery regression suite.

## Provider implementation boundary

Infrastructure retains IPersistenceSession, IEntitySet, IProviderQuery/IProviderQueryExecutor
and provider update expressions as low-level collaboration for shared store implementations.
These are not relocated domain repository contracts or a business API. The session has only
Set<T>, async save and async transaction begin. Entity sets no longer implement ICollection
or expose Attach/Update/Range/Clear/CopyTo. Core has no Infrastructure reference.
Concrete EF/NHibernate/native transaction mechanisms remain available in host adapters.
The query helpers require the Infrastructure namespace rather than adding global System.Linq persistence extensions. They are shipped by Infrastructure, including provider
fallback tests; pure expression mapping remains in Application.

This change removes the ORM abstraction from the business dependency boundary rather than
claiming every ORM has identical execution semantics. Native batch SQL can execute before
SaveChanges, bypass tracking, and require an explicit surrounding transaction when mixed
with other writes. Keep such operations inside reviewed storage implementations.

## Breaking migration

Remove Domain IRepository/IAsyncQueryable/IAsyncQueryExecutor/update setters and Application
IDbContext/IDbContextFactory/IDbContextTransaction/async query helpers. Define domain-specific
repositories and Application DTO query ports; inject IUnitOfWork for saving and optionally
ITransactionManager for explicit transactions. Move native persistence and query composition
to host/provider implementations. There are no forwarding aliases for the removed core APIs.

Infrastructure implementations use the provider collaboration contracts and register
AddPersistenceServices; application code must not inject those contracts as a replacement
business facade. Update custom provider factories and mocks together. Range/tracking/savepoint
operations use concrete native APIs. Upgrade all packages, ports, adapters and tests together;
rollback restores the previous coherent set. This API migration requires no database schema change.
