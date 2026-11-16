# Native EF Core configuration and NOF policy

[简体中文](../../zh-CN/design/efcore-configuration.md) · [Persistence boundaries](persistence-boundaries.md)

NOF retains tenant-aware context creation, optional soft deletion, value-object conventions, the scoped unit of work/transactions and native Outbox stores. EF Core owns provider selection, connections, logging, interceptors, retry settings, tracking and model mapping. The adapter exposes one native configuration callback instead of forwarding those options through NOF methods.

```csharp
builder.UseDbContext<ConfigurationDbContext>((creation, ef) =>
{
    var connections = creation.Services.GetRequiredService<ITenantDatabaseCatalog>();
    ef.UseNpgsql(connections.GetConnectionString(creation.DbContextType, creation.TenantId),
        postgres => postgres.EnableRetryOnFailure());
    ef.EnableDetailedErrors();
    ef.AddInterceptors(creation.Services.GetRequiredService<AuditInterceptor>());
})
.ConfigureNOFDbContextOptions(nof =>
{
    nof.TenantMode = TenantMode.DatabasePerTenant;
    nof.SoftDeleteEnabled = true;
})
.MigrateOnStart();
```

`ef` is the original `DbContextOptionsBuilder<ConfigurationDbContext>`. `DbContextCreationContext` supplies the factory's scoped services, concrete type, normalized explicit tenant and tenant mode. The callback runs for every creation, including `ITenantDbContextFactory<T>.CreateDbContext(tenant)`. Use its TenantId when selecting an explicit tenant's connection; it may differ from Context.Current. No connection-string resolver delegate or intermediate string pipeline is needed. Registration captures no scoped service instances.

For a fixed connection, call the provider directly in the callback:

```csharp
builder.UseDbContext<NotificationsDbContext>((_, ef) =>
    ef.UseSqlite(builder.Configuration.GetConnectionString("notifications")))
    .ConfigureNOFDbContextOptions(nof => nof.TenantMode = TenantMode.SharedDatabase);
```

Repeated configuration callbacks for the same context type compose in registration order; callbacks for distinct types are isolated. NOF installs its EF integration defaults before native callbacks. `NOFDbContextOptions` contains only TenantMode and SoftDeleteEnabled, with the same defaults as before. The primary scoped aliases and shared transaction behavior remain; selecting a context does not create an independent business unit of work.

## Model and lifecycle capabilities

Use native OnModelCreating, EntityTypeBuilder, Property, HasKey/HasIndex, relationships, owned mappings and IEntityTypeConfiguration<T> directly. HasSoftDelete remains a NOF annotation because EF Core has no equivalent NOF deletion policy. Existing NOF model contributors continue to apply optional framework message entities and feature model configuration; no second general query/model/update facade is added.

MigrateOnStart remains the explicit NOF host/tenant startup orchestration. It invokes native migrations, waits for host migration and performs the tenant pass after startup. ITenantProvider still supplies the catalog. Native Database.MigrateAsync/EnsureCreated and context lifetime APIs remain directly available. The SQLite initialization and memory-connection lifetime behavior are unchanged by this API consolidation.

Custom creation lifecycles can still use UseDbContext<TContext,TFactory>(nativeCallback), deriving from NOFDbContextFactory<TContext>. Connection routing alone belongs in the native callback; it does not require a factory subclass.

## Breaking API migration

Remove WithTenantMode, WithSoftDelete, WithConnectionString, WithConnectionStringResolver and WithOptions from EF registrations. Replace DbContextConfigurationOptions with NOFDbContextOptions; only NOF feature flags remain. Replace DbContextConnectionStringResolutionContext/the resolver delegate with DbContextCreationContext in the native callback. Move provider/connection configuration into UseDbContext<T>'s callback, and NOF flags into ConfigureNOFDbContextOptions. There are no compatibility aliases.

Connection templates are now explicit application decisions. Existing `{tenantId}` templates can still use the shared DbConnectionStringTemplateResolver.ResolveTenantId inside a native UseNpgsql/UseSqlite call; it is no longer an EF configuration property. Capture generated database names or connection instances at registration when they must remain stable across context creations.

No schema or data migration is introduced. Preserve existing TenantMode and SoftDeleteEnabled settings and native mappings to keep the same columns, filters, keys and deletion semantics. The Sample retains its PostgreSQL per-tenant configuration, SQLite shared receiver and explicit migration startup.
