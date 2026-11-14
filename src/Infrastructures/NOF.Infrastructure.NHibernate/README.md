# NOF.Infrastructure.NHibernate

NHibernate persistence package for the [NOF Framework](https://github.com/XmmShp/NOF).

## Overview

`NOF.Infrastructure.NHibernate` implements NOF feature stores and the application unit-of-work boundary with native NHibernate APIs.

Current scope:

- `UseNHibernate()`
- a shared native `ISession`, feature-specific Inbox/Outbox stores and `NHibernateUnitOfWork`
- native mapping-by-code configuration through `Action<ModelMapper>`
- tenant-aware connection-string resolution for `TenantMode.DatabasePerTenant`

Current limitations:

- `TenantMode.SharedDatabase` is not supported yet
- optional framework schema initialization uses NHibernate `SchemaUpdate`; native configuration retains its own schema options

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

Business handlers use aggregate repositories, DTO query ports and IUnitOfWork. Infrastructure uses native ISession/LINQ DML and mapping APIs. No cross-ORM session, query executor or model builder remains. Optional OIDC storage is supplied by NOF.Hosting.AspNetCore.Extension.OidcServer.NHibernate with AddOidcNHibernate. See [persistence boundaries](../../../docs/en/design/persistence-boundaries.md).
