# NOF.Infrastructure.NHibernate

NHibernate persistence package for the [NOF Framework](https://github.com/XmmShp/NOF).

## Overview

`NOF.Infrastructure.NHibernate` provides an NHibernate-based implementation of the NOF persistence abstractions.

Current scope:

- `UseNHibernate()`
- NHibernate-backed `IPersistenceSession` and `IPersistenceSessionFactory`
- contributor-based entity mapping through `IDbContextModelCreatingContributor`
- tenant-aware connection-string resolution for `TenantMode.DatabasePerTenant`

Current limitations:

- `TenantMode.SharedDatabase` is not supported yet
- `AsNoTracking()` currently returns the same LINQ query shape without a dedicated no-tracking session
- schema management is limited to NHibernate `SchemaUpdate`

In `DatabasePerTenant` mode, the host database and all tenant databases use the same model and
therefore the same schema. Tenant databases must accept tables used only by host-level features as
an intentional, potentially empty schema superset. This avoids separate host and tenant model,
schema-update, provider, and deployment paths.

## Usage

```csharp
using NHibernate.Dialect;
using NHibernate.Driver;
using NOF.Infrastructure.NHibernate;

var builder = WebApplication.CreateBuilder(args).AddNOF();

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

Business handlers use aggregate repositories, DTO query ports and IUnitOfWork. IPersistenceSession and its query helpers are infrastructure collaboration only. See [persistence boundaries](../../../docs/en/design/persistence-boundaries.md).
