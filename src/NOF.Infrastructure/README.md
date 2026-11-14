# NOF.Infrastructure

Unified infrastructure entry package for the [NOF Framework](https://github.com/XmmShp/NOF).

## Overview

`NOF.Infrastructure` installs operation execution and common authorization/tenant policy. Cache, object storage, backplane, ID algorithms and messaging are explicit capabilities, selected through typed selectors. Authentication and telemetry exporters belong to the native host.

```csharp
builder.Services.AddCacheService().AddMemoryCacheService();
builder.Services.AddObjectStorage().AddFileSystemObjectStorage();
builder.Services.AddBackplane().AddMemoryBackplane();
builder.Services.AddIdGenerator().AddSnowflakeIdGenerator();
```

See [host composition and identity migration](../../docs/en/design/host-composition.md) ([简体中文](../../docs/zh-CN/design/host-composition.md)).

Application services use AddApplicationService<TService>() and application-part initialization for generated operation implementations. Generated Service.Operation.InvokeAsync uses the existing policy pipeline; no client interfaces or implementations are generated, and undecorated contracts expose no HTTP routes. See the [migration guide](../../docs/en/design/application-requests.md).

## Usage

Add a single reference to `NOF.Infrastructure` in host or infrastructure-adapter projects.

## Installation

```shell
dotnet add package NOF.Infrastructure
```

## Built-in Capabilities

This package includes:

- in-memory cache (`ICacheService` + `MemoryCacheServiceRider`)
- local file system object storage (`IObjectStorage` + `FileSystemObjectStorageRider`), with an optional `MemoryObjectStorageRider` for tests
- in-memory backplane (`IBackplane` + `MemoryBackplane`)
- direct MemoryMessageClient queue/subscription receipt acceptance
- message-only in-memory persistence for development (`services.AddInMemoryMessagingPersistence()`)
- in-process event publisher (`IEventPublisher`)
- feature-specific IInboxStore/IOutboxStore contracts, implemented through native APIs in provider packages
- outbox / inbox entities and transactional message background services

The default in-memory cache implementation is isolated per NOF host:

- cache data lives in `MemoryCacheServiceRiderState`
- local `GetOrSetAsync(...)` locks live in `CacheServiceLocalLockState`
- both are registered as DI singletons instead of process-wide `static` state

The default backplane implementation is also host-local:

- subscriptions live in `MemoryBackplaneState`
- published messages are delivered only to subscribers inside the same NOF host process

The explicitly selected file-system object store persists content and metadata locally under `AppContext.BaseDirectory/App_Data/objects`. Configure a writable, persistent directory through `builder.Services.AddObjectStorage().AddFileSystemObjectStorage(options => options.RootPath = "/data/objects")`. Relative paths resolve against the working directory. The directory is provider-owned: object names are hashed, and files contain both metadata and content; do not modify its files or add symbolic links. Writes replace complete objects atomically, and reads retain a snapshot across replacement or deletion. Listing uses ordinal key order and prefix matching; it scans the bucket's metadata files.

For isolated tests, use `builder.Services.AddObjectStorage().AddMemoryObjectStorage()`. `Host.CreateApplicationBuilder().AddNOFTest()` selects this automatically; state is shared between scopes within one host and discarded when that host is disposed. Replace `IObjectStorageRider` in a provider package to connect another backend while keeping application code unchanged. For AWS S3 and S3-compatible services, reference `NOF.Infrastructure.AmazonS3`:

```csharp
builder.Services.AddObjectStorage().AddAmazonS3ObjectStorage(options =>
{
    options.Region = "ap-southeast-1";
});
```

Object keys can be isolated by tenant through `ObjectStorageOptions.KeyPrefix`:

```csharp
builder.Services.Configure<ObjectStorageOptions>(options =>
    options.KeyPrefix = "tenants/{tenantId}/");
```

`IObjectStorage.IgnoreKeyPrefix()` provides an explicit administrative view over physical keys when cross-tenant access is required.

`IObjectStorage` also exposes `SupportsPresignedRequests`, `CreatePresignedUploadAsync`, and
`CreatePresignedDownloadAsync` for direct HTTP transfers. Returned requests include the URL,
HTTP method, required headers, and requested expiration. They apply the same tenant key prefixes.
S3 supports signing; the built-in file system and memory riders report `false` and throw
`NotSupportedException` because they do not expose HTTP endpoints. The default interface methods
preserve compatibility for custom riders that have not implemented signing. Authorize each signing
request in the application before issuing a URL. See the S3 provider README for browser usage.

## Identity and tenant authorization

The native host authenticates HTTP requests. Configure AddAuthentication().AddJwtBearer(...), UseAuthentication/UseAuthorization and endpoint RequireAuthorization when native challenge/forbid behavior is required. NOF copies only HttpContext.User, then runs cross-origin business authorization before constructing handlers. It never validates JWTs or treats Context.Items as credentials.

TenantAuthorizationOptions can require an authenticated non-host tenant and reject conflicting tenant routing input. Both checks apply uniformly to all invocation origins, with no platform-operation exception. Missing routing metadata still normalizes to host; a trusted identity tenant takes precedence. Use an application-owned authorization handler for membership/catalog checks and an explicit application filter for special execution context. There is no host-tenant annotation or metadata key interpretation.

Local invocation isolates the scoped principal; reliable messages persist no user identity. Restrict broker access and establish trusted producer/tenant policy at the adapter boundary. See [native host composition](../../docs/en/design/host-composition.md) and the [HTTP identity regression](../../tests/NOF.Hosting.AspNetCore.Tests/TenantRequestAuthorizationTests.cs).

## Server operation invocation

Register the application service once and call the generated server operation entry:

```csharp
builder.AddApplicationService<OrderService>();
var order = await OrderService.GetOrder.InvokeAsync(services, request, context, ct);
```

`IOperationInvoker` is registered as scoped infrastructure. Static operation
entries delegate to it; no client interfaces or Local/HTTP Client implementations are
generated. Invocation opens and asynchronously disposes a fresh scope for daemon
services, inbound policies and the handler. Context items flow directly and the
caller's identity is copied as an isolated snapshot; persistence dependencies are
not shared. Local identity is already verified, so token authentication is skipped
while application authorization and tenant policies still run. Authorization and tenant normalization precede
handler resolution. Complete invocation returns `Task<object?>`: match a business value/union or a generated filter failure. Business handlers keep their closed typed response. Infrastructure owns filter interfaces, Global/Service/UseCase Use/Exclude rules and the effective failure catalog; InvocationContext has no mutable result slot. See [invocation extensions](../../docs/en/design/invocation-extensions.md).

Plain contracts expose no HTTP routes. Ordinary HTTP feature extensions in the HTTP
host own routing and use HttpContext.InvokeAsync with generated operation descriptors.
Legacy HTTP RPC transports, IApplicationServiceTransport and automatic service-to-route
initialization are deleted. Host endpoint modules explicitly select service operations.
See the [REST implementation](../../docs/en/design/configuration-rest.md).

## Persistence Providers

`NOF.Infrastructure` no longer ships a built-in EF Core implementation. Database persistence is provided by adapter packages such as `NOF.Infrastructure.EntityFrameworkCore`.

After adding the EF Core package, persistence is configured through `UseDbContext<TDbContext>()` and `EFCoreSelector`:

```csharp
using Microsoft.EntityFrameworkCore;
using NOF.Infrastructure.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args).AddNOF();

builder.UseDbContext<AppDbContext>()
    .WithTenantMode(TenantMode.DatabasePerTenant)
    .WithConnectionString(builder.Configuration.GetConnectionString("postgres")
        ?? throw new InvalidOperationException("Connection string 'postgres' not found."))
    .WithOptions(static (optionsBuilder, connectionString) => optionsBuilder.UseNpgsql(connectionString))
    .MigrateOnStart();
```

Available configuration methods:

- `WithTenantMode(...)`
- `WithConnectionString(...)`
- `WithConnectionStringResolver(...)`
- `WithOptions(...)`
- `MigrateOnStart()`

The resolver receives the normalized tenant identifier, concrete `DbContext` type, tenant mode, fallback template, and scoped services. Use it for tenant catalogs, secret stores, or shard maps; the template remains the convenient static default.

`MigrateOnStart()` waits for host database migration before hosted services start. In
`DatabasePerTenant` mode, it then migrates tenants from `ITenantProvider` sequentially in the
background after the application starts. Each migration uses an isolated scope and the tenant's
resolved connection string; one tenant failure is logged without stopping the remaining migrations.
`SharedDatabase` mode migrates only the host/shared database. For newly provisioned tenants or
explicit retries, resolve `ITenantDbContextFactory<TDbContext>` from a scope and call
`MigrateAsync(tenantId, cancellationToken)`.

Transactional inbox and outbox processors also need the application's tenant catalog to
poll database-per-tenant message tables. Register an `ITenantProvider`
that enumerates tenant IDs from durable storage:

```csharp
builder.Services.AddSingleton<ITenantProvider, AppTenantCatalog>();
```

`AppTenantCatalog.GetTenantIdsAsync` should return every active tenant database, including
tenants with messages written before this process started. NOF always polls the host database
and deduplicates normalized tenant IDs. The default provider returns no additional tenants.
`ITenantProvider` replaces `ITransactionalMessageTenantProvider`; update custom implementations
and registrations to the new name. The same catalog is used by startup tenant migrations and
inbox/outbox cleanup. Migrations enumerate once after startup; tenant migration failures require
an explicit retry or a restart. For messaging, a catalog failure is logged and retried
at the next polling interval; an error in one tenant database does not prevent other tenants
from being processed.

## SQLite

For SQLite, provide the provider configuration via `WithOptions(...)`:

```csharp
builder.UseDbContext<AppDbContext>()
    .WithTenantMode(TenantMode.SharedDatabase)
    .WithConnectionString(builder.Configuration.GetConnectionString("sqlite")
        ?? throw new InvalidOperationException("Connection string 'sqlite' not found."))
    .WithOptions(static (optionsBuilder, connectionString) => optionsBuilder.UseSqlite(connectionString))
    .MigrateOnStart();
```

## In-Memory SQLite

For tests or lightweight local scenarios, `NOF.Infrastructure.EntityFrameworkCore` provides `AddNOFEntityFrameworkCore()` to register the default SQLite in-memory persistence.

## License

Apache-2.0

## Invocation execution

NOF hosts select `InMemoryInvocationPipeline` by default. `AddInvocationFilter<T>(order)` registers a scoped extension definition; `UseInMemoryInvocationPipeline()` selects the optional engine without replacing a host-supplied `IInvocationPipeline`. Service and message adapters share this boundary. The old five middleware families and outbound contexts are removed. `MessageDelivery` directly prepares identity, tenant and trace metadata for immediate and transactional sends. See [execution and migration](../../docs/en/design/invocation-extensions.md).

Business handlers depend on aggregate repositories, DTO query ports and IUnitOfWork. Native DbContext/ISession and query/model APIs remain in host/provider implementations. The generic session/query/update/model facade and general memory ORM are removed. Inbox/Outbox workers use resource-specific stores. See [persistence boundary migration](../../docs/en/design/persistence-boundaries.md).

See [unified operations](../../docs/en/design/unified-operations.md) for IOperationExecutor, host message bindings, durable consumer IDs and stored-route migration.

Immediate messages use concrete MemoryMessageClient or native RabbitMQ IChannel with explicit destinations. Generic IOutbox intents are interpreted by OutboxHandler<T> adapters automatically registered through AddApplicationPart (public MessageType and optional SchemaVersion constants); AddOutbox() explicitly enables the capability; AddRabbitMQ does not replace the memory client. Transport-owned queue/subscription endpoints remain separate. See [transactional outbox](../../docs/en/design/transactional-outbox.md).

Durable workers and model contributors are installed only by AddOutbox/AddInbox (enabled by transport endpoint configuration). Set TransactionalMessageOptions.Outbox/Inbox.RunWorkers=false for staging/receiving-only hosts. See [outbox capability assembly](../../docs/en/design/transactional-outbox.md).
