# NOF.Infrastructure.NHibernate

[简体中文](../../zh-CN/packages/nhibernate.md)

Native NHibernate persistence and Outbox integration.

UseNHibernate selects native configuration, connection and model mapping callbacks. Domain-owned repositories/query ports use ISession in infrastructure; IUnitOfWork and optional transactions share the scoped session with Outbox stores. Configure tenant and value-object mappings through the provider. DatabasePerTenant uses one model/schema for host and tenants; host-only tables may be empty in tenant databases. The framework does not wrap native query APIs or persist consumer receipts.

## Installation

```sh
dotnet add package NOF.Infrastructure.NHibernate
```

## Usage and boundaries

- [persistence-boundaries](../design/persistence-boundaries.md)
- [transactional-outbox](../design/transactional-outbox.md)

Select providers in the native host; do not add infrastructure dependencies to Contract or Domain. See [the complete Sample](../design/sample-workbench.md).

## Native configuration

SharedDatabase is currently unsupported. Optional schema initialization uses native NHibernate SchemaUpdate; review schema changes and deployment ownership before enabling it.

```csharp
builder.UseNHibernate()
    .WithConnectionString("Data Source=nof-{tenantId}.db")
    .WithOptions((configuration, connectionString) =>
    {
        configuration.DataBaseIntegration(db =>
        {
            db.ConnectionString = connectionString;
            db.Dialect<SQLiteDialect>();
            db.Driver<SQLite20Driver>();
        });
    })
    .BuildSchemaOnInitialize();
```
