# NOF.Infrastructure.EntityFrameworkCore

Entity Framework Core persistence package for the [NOF Framework](https://github.com/XmmShp/NOF).

## Overview

`NOF.Infrastructure.EntityFrameworkCore` contains the EF Core-specific persistence implementation that used to live inside `NOF.Infrastructure`, including:

- `NOFDbContext`
- `UseDbContext<TDbContext>()`
- `EFCoreSelector`
- tenant-aware model customization and factory services
Message endpoints directly execute their operation with restored Context and the existing policy/filter pipeline. Business code owns idempotency and commit boundaries; adapters own acknowledgement, retry and dead-letter behavior. NOF Inbox storage and workers are removed. AddOutbox explicitly enables outgoing intents only; configure flat OutboxOptions. See [transactional Outbox and migration](https://github.com/io7-cn/NOF/blob/main/docs/en/design/transactional-outbox.md).
- SQLite in-memory default persistence registration

## Installation

```shell
dotnet add package NOF.Infrastructure.EntityFrameworkCore
```

## Usage

```csharp
using Microsoft.EntityFrameworkCore;
using NOF.Hosting;
using NOF.Infrastructure;
using NOF.Infrastructure.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args).AddNOF();

builder.UseDbContext<AppDbContext>((creation, ef) => ef.UseNpgsql(
        DbConnectionStringTemplateResolver.ResolveTenantId(builder.Configuration.GetConnectionString("postgres")
            ?? throw new InvalidOperationException("Connection string 'postgres' not found."), creation.TenantId)))
    .ConfigureNOFDbContextOptions(nof => nof.TenantMode = TenantMode.DatabasePerTenant)
    .MigrateOnStart();
```

`MigrateOnStart()` awaits migration of the **host** database before hosted services start. A host
migration failure prevents initialization from completing. In `DatabasePerTenant` mode, after
`ApplicationStarted`, NOF enumerates `ITenantProvider` and migrates the other tenant databases
sequentially in the background, using a fresh scope and tenant context for each database.
Custom connection-string resolvers are used as usual. Startup does not wait for tenant migrations.
In `SharedDatabase` mode only the host/shared database is migrated.

Register the application's durable tenant catalog once for both migration and transactional messaging:

```csharp
builder.Services.AddSingleton<ITenantProvider, AppTenantCatalog>();
```

`ITenantProvider` is in `NOF.Infrastructure` and replaces `ITransactionalMessageTenantProvider`;
update implementations and DI registrations to the new name. Its `GetTenantIdsAsync` method is unchanged.
The default `HostTenantProvider` returns no additional tenants. Catalog implementations must support
concurrent enumeration and should create scopes to query the host database. Return the tenants whose
databases should be migrated and whose transactional messages should be processed.

Host entries and duplicate normalized tenant IDs are skipped by the background migrator. A tenant
migration failure is logged and does not prevent remaining tenants from migrating. Shutdown cancels
the operation. Catalog enumeration failure is logged and ends the migration pass. This is one pass
per application start, not a recurring provisioning service; tenants may be temporarily unavailable
until their migrations complete. Use the explicit tenant migration API below for newly created tenants
or to retry failures without restarting. Application-specific provisioning status and backfills remain
the application's responsibility.

### Database-per-tenant schema policy

NOF uses one `DbContext` model and one migration chain for the host database and every tenant
database. Their database schemas are therefore identical. Tables that are used only by host-level
features, such as some OIDC configuration tables, are still created in tenant databases and may
remain empty. Tenant databases must accept this schema superset.

This is an intentional engineering tradeoff. NOF does not maintain separate host and tenant model
variants because doing so would substantially increase model caching, migration generation,
deployment orchestration, provider integration, and testing complexity. Data placement remains a
Message endpoints directly execute their operation with restored Context and the existing policy/filter pipeline. Business code owns idempotency and commit boundaries; adapters own acknowledgement, retry and dead-letter behavior. NOF Inbox storage and workers are removed. AddOutbox explicitly enables outgoing intents only; configure flat OutboxOptions. See [transactional Outbox and migration](https://github.com/io7-cn/NOF/blob/main/docs/en/design/transactional-outbox.md).
tables are not necessarily empty in tenant databases because tenant-scoped transactional messaging
may use them.

For lightweight local or test scenarios, call `AddNOFEntityFrameworkCore()` to register the default SQLite in-memory persistence.

## Dynamic connection resolution

### Sharing native operations with Application persistence

`UseDbContext<AppDbContext>()` resolves `AppDbContext`, `NOFDbContext`, `DbContext`,
and the infrastructure session / application IUnitOfWork adapter over the same scoped instance. A Host
implementation can inject `AppDbContext` to execute provider-specific SQL and join
the transaction started through ITransactionManager. Factory-created contexts are independent.
See [persistence boundaries](../../../docs/design/repository-uow.md) for connection,
transaction, tenant, and retry requirements.

This corrects earlier registrations that created separate contexts for those service
types. Code relying on independent change trackers must explicitly create separate
contexts instead. Select the tenant before resolving any scoped persistence service.

Configure the native provider in UseDbContext<T>((creation, ef) => ...). A tenant catalog, secret store or shard map can be resolved from the current scoped services directly:

```csharp
builder.UseDbContext<AppDbContext>((creation, ef) =>
{
    var catalog = creation.Services.GetRequiredService<ITenantDatabaseCatalog>();
    ef.UseNpgsql(catalog.GetConnectionString(creation.DbContextType, creation.TenantId));
})
.ConfigureNOFDbContextOptions(nof => nof.TenantMode = TenantMode.DatabasePerTenant)
.MigrateOnStart();
```

DbContextCreationContext exposes normalized explicit TenantId, DbContextType, TenantMode and scoped Services; ef is the original typed DbContextOptionsBuilder<T>. The callback runs for every context creation. Native callbacks compose in registration order for each context type, without leaking between types. Cache remotely acquired connection metadata in an application dependency when synchronous creation needs it.

## Custom tenant migration orchestration

`UseDbContext<TDbContext>()` registers `ITenantDbContextFactory<TDbContext>`. It creates a strongly
typed context for an explicit tenant without changing `Context.Current`, and reuses the configured
native typed options callback for that explicit tenant. The `MigrateAsync` extension makes it
convenient to build a deployment job around an application-owned tenant catalog:

```csharp
public sealed class TenantDatabaseMigrator(
    IServiceScopeFactory scopeFactory,
    ITenantCatalog tenantCatalog)
{
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        await foreach (var tenant in tenantCatalog.GetAllAsync(cancellationToken))
        {
            await using var scope = scopeFactory.CreateAsyncScope();
            var factory = scope.ServiceProvider
                .GetRequiredService<ITenantDbContextFactory<AppDbContext>>();

            await factory.MigrateAsync(tenant.Id, cancellationToken);
        }
    }
}
```

The application owns tenant discovery, retries, concurrency limits, and deployment coordination.
The factory owns tenant normalization and connection resolution; the migration helper owns context
disposal and delegates locking and migration execution to EF Core.

For a tenant readiness gate, persist `Pending`, `Migrating`, `Ready` or `Failed` plus
the schema version in the Host tenant catalog. Use explicit migration orchestration,
and publish `Ready` only after `MigrateAsync` and any required backfill complete.
On failure, record the error and retry the same explicit API in a fresh scope.
Provision new tenants through that same lifecycle. Check the catalog's readiness and
version in `IInboundAuthorizationHandler` before resolving tenant persistence.
Process health and successful Host migration do not establish tenant readiness.
Use a deployment migration job or distributed lease to coordinate multiple replicas;
the factory does not expose a distributed readiness service, and provider migration
locks do not coordinate application backfills or status publication.

### Custom database context factory

`NOFDbContextFactory<TDbContext>` is public and its typed context creation methods are virtual.
Register a derived factory with the two-type-parameter `UseDbContext` overload when context creation
requires behavior beyond native options configuration:

```csharp
public sealed class AppDbContextFactory(
    IServiceProvider services,
    ITenantDatabaseProvisioner provisioner)
    : NOFDbContextFactory<AppDbContext>(services)
{
    public override AppDbContext CreateDbContext(string tenantId)
    {
        provisioner.EnsureProvisioned(tenantId);
        return base.CreateDbContext(tenantId);
    }
}

builder.UseDbContext<AppDbContext, AppDbContextFactory>((creation, ef) =>
    ef.UseNpgsql(creation.Services.GetRequiredService<ITenantDatabaseCatalog>()
        .GetConnectionString(creation.DbContextType, creation.TenantId)));
```

The custom factory is registered as itself, `NOFDbContextFactory<TDbContext>`,
`ITenantDbContextFactory<TDbContext>` and EF Core's `IDbContextFactory<TDbContext>`.
Use the native options callback when only database routing differs; derive the factory when the context creation lifecycle itself must change.

## Soft delete

Soft delete is disabled by default. Enable it explicitly for every supported root entity type in a `DbContext` with `ConfigureNOFDbContextOptions(nof => nof.SoftDeleteEnabled = true)`, or opt in an individual root entity type with `HasSoftDelete()`:

```csharp
using Microsoft.EntityFrameworkCore.Metadata.Builders;

builder.UseDbContext<AppDbContext>()
    .ConfigureNOFDbContextOptions(nof => nof.SoftDeleteEnabled = true);

protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    base.OnModelCreating(modelBuilder);

    modelBuilder.Entity<Order>(entity =>
    {
        entity.HasSoftDelete();
    });
}
```

The entity-level setting takes precedence over the `DbContext` setting. Call `HasSoftDelete(false)` to opt a root entity type out when soft delete is enabled for the context. Owned entity types inherit the lifecycle of their owner and cannot be configured independently.

Applications upgrading from a version where soft delete was enabled by default should add
`ConfigureNOFDbContextOptions(nof => nof.SoftDeleteEnabled = true)` before generating or applying further migrations if they intend to preserve
their existing delete columns, indexes, filters, and behavior.

Business handlers use aggregate repositories, DTO query ports and IUnitOfWork. Infrastructure adapters use native DbContext and EF query/mapping APIs. The cross-ORM session/query/model layer is removed; EF-specific model extensions implement IEFCoreModelConfiguration with ModelBuilder. See [persistence boundaries](../../../docs/en/design/persistence-boundaries.md).

## Native configuration boundary and migration

WithTenantMode/WithSoftDelete/WithConnectionString/WithConnectionStringResolver/WithOptions and DbContextConfigurationOptions are removed. Configure NOF feature flags through ConfigureNOFDbContextOptions and the two-property NOFDbContextOptions; configure all EF/provider settings directly in UseDbContext<T>'s native callback. DbContextCreationContext replaces the string-resolution-only context/delegate. Preserve existing feature flags and native mappings; no schema/data migration is introduced. See [native EF configuration](../../../docs/en/design/efcore-configuration.md) and its [Chinese edition](../../../docs/zh-CN/design/efcore-configuration.md).
