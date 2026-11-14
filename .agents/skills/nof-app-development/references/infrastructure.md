# Infrastructure integration

Business code depends on domain aggregate repositories, Application DTO query ports and
IUnitOfWork. Infrastructure-only IPersistenceSession/IEntitySet/IProviderQuery are provider
collaboration for shared stores, never a replacement business facade. See
`docs/en/design/persistence-boundaries.md` and its Chinese edition.


Concrete providers are host concerns. Application handlers depend on domain-specific repositories, DTO query ports, IUnitOfWork, ICacheService and messaging interfaces.

## EF Core + PostgreSQL

Packages:

```bash
dotnet add package NOF.Infrastructure.EntityFrameworkCore
dotnet add package Npgsql.EntityFrameworkCore.PostgreSQL
```

Registration:

```csharp
using Microsoft.EntityFrameworkCore;
using NOF.Hosting;
using NOF.Infrastructure.EntityFrameworkCore;

builder.UseDbContext<AppDbContext>()
    .WithTenantMode(TenantMode.DatabasePerTenant)
    .WithConnectionString(builder.Configuration.GetConnectionString("postgres")
        ?? throw new InvalidOperationException("Connection string 'postgres' not found."))
    .WithOptions(static (optionsBuilder, connectionString) => optionsBuilder.UseNpgsql(connectionString))
    .MigrateOnStart();
```

`MigrateOnStart()` migrates the context resolved during host initialization. It does not
enumerate application-defined tenant databases in `DatabasePerTenant` mode.

`DatabasePerTenant` deliberately uses the same model and migration chain for the host and every
tenant database. Their schemas are identical, so tenant databases must accept tables used only by
host-level features as a potentially empty schema superset. Inbox and outbox tables can contain
tenant-scoped transactional messages and are not guaranteed to be empty.

`NOFDbContext` applies the registered model contributors for inbox and outbox entities. It also supplies value-object conversion/length conventions, multi-tenancy, and opt-in soft delete. Soft delete is disabled by default; use `.WithSoftDelete(true)` for a context-wide opt-in or `HasSoftDelete()` in EF model configuration for an entity-level opt-in.

The host may inject EF `DbContext` or the concrete context, but application handlers use owned aggregate/read ports and IUnitOfWork.

For dynamic database selection, replace the template lookup with `.WithConnectionStringResolver(...)`. Its context provides the normalized tenant ID, concrete `DbContext` type, tenant mode, fallback template, and scoped services, so the resolver can consult a tenant catalog, secret store, or shard map before `WithOptions(...)` configures the provider.

For an application-owned migration job, resolve `ITenantDbContextFactory<TDbContext>` in a scope
and call `MigrateAsync(tenantId, cancellationToken)` for each tenant. The application remains in
control of tenant enumeration, retries, and concurrency, while the factory reuses the configured
tenant connection resolution and provider options.

When the context creation lifecycle itself must be customized, derive from
`NOFDbContextFactory<TDbContext>`, override `CreateDbContext()` and/or
`CreateDbContext(string tenantId)`, and register it with
`UseDbContext<TDbContext, TDbContextFactory>()`. Prefer `WithConnectionStringResolver(...)` when
only tenant database routing differs.

## Default In-Memory Infrastructure

`WebApplication.CreateBuilder(args).AddNOF()` calls `AddNOF()`, which registers in-memory persistence, cache, command, notification, and backplane defaults. Use those defaults for lightweight development/tests; selecting a provider replaces the corresponding abstractions.

`builder.AddNOFEntityFrameworkCore()` is a separate EF Core SQLite in-memory option. It is useful when code needs EF-specific behavior rather than the provider-neutral in-memory store.

## Redis Cache and Backplane

Package:

```bash
dotnet add package NOF.Infrastructure.StackExchangeRedis
```

Registration:

```csharp
builder.Services.AddRedisCache(builder.Configuration.GetConnectionString("redis")
    ?? throw new InvalidOperationException("Connection string 'redis' not found."));
```

This replaces `ICacheServiceRider`; callers continue to inject `ICacheService`. Use `AddRedisBackplane(...)` separately when `IBackplane` should also use Redis.

## RabbitMQ

Package:

```bash
dotnet add package NOF.Infrastructure.RabbitMQ
```

Registration:

```csharp
builder.Services.AddRabbitMQ(factory => factory.Uri = new Uri(builder.Configuration.GetConnectionString("rabbitmq")!));
```

This registers native ConnectionFactory/IConnectionFactory and lazy RabbitMQConnection access only. Configure native TLS, AMQP URI, authentication, timeouts and recovery through ConnectionFactory. RabbitMQOptions and RabbitMQConnectionManager are removed. ConfigureRabbitMQ may be called repeatedly before Build; its fluent Topology callbacks and MapQueue endpoints compose. Framework StartingAsync executes every native topology callback in registration order before consumers start. Mapping a queue enables Inbox; topology-only configuration does not. Each MapQueue can configure its native channel (for example BasicQosAsync) and receipt-failure requeue policy. Backplane registration remains independent through AddRabbitMQBackplane().

See `docs/en/design/rabbitmq-sample.md` (and its Chinese edition): sample topology is an inline native lambda alongside queue mappings in one feature entry, with no application IHostedLifecycleService or separate topology holder. Infrastructure uses MessageDelivery with full OutboxDeliveryContext around native BasicPublishAsync. Native channels are caller-owned; RabbitMQConnection.GetAsync returns a shared connection that callers must not dispose.

## OAuth/OIDC Authority

Package:

```bash
dotnet add package NOF.Hosting.AspNetCore.Extension.OidcServer
```

Registration:

```csharp
using NOF.Hosting.AspNetCore.Extension.OidcServer;

builder.AddOidcServer(options =>
{
    options.Issuer = "https://auth.example.com/oauth2";
    options.AccessTokenAudience = "my-app";
    options.SigningKeyEncryptionKey = builder.Configuration["NOF:OidcServer:SigningKeyEncryptionKey"]
        ?? throw new InvalidOperationException("OIDC signing-key encryption key not found.");
})
.AddPublicClient(
    "my-app-ui",
    ["openid", "profile", "my-app.read"],
    redirectUris: ["https://app.example.com/oauth/callback"]);
```

`AddOidcServer(...)` registers persistent signing keys, revoked refresh tokens, clients, background cleanup/rotation, and an initialization step. The initialization step maps the OIDC/OAuth endpoints automatically and bootstraps configured clients after database migration. Supply a durable persistence provider in production.

Replace `IOAuthAuthorizeEndpoint` and `IOAuthSubjectService` with application implementations when using authorization-code/OIDC identity flows. Device, confidential, private-key JWT, and dynamic-registration surfaces are also available through the OIDC server package.

## JWT Resource Server

```csharp
builder.Services.AddAuthenticationResourceServer(options =>
{
    options.AuthorizationServerIssuer = "https://auth.example.com/oauth2";
    options.ExpectedIssuer = "https://auth.example.com/oauth2";
    options.Audience = "my-app";
    options.RequireHttpsMetadata = true;
});
```

The resource server discovers OAuth authorization-server metadata and JWKS from `AuthorizationServerIssuer`. It populates `IUserContext` and the tenant pipeline for RPC, command, and notification handling.

## Object Storage and Direct Transfers

Application code uses `IObjectStorage`; the default rider stores objects on the local file system.
Use `AddMemoryObjectStorage()` for tests or `AddAmazonS3ObjectStorage(...)` for an S3 endpoint.
`CreatePresignedUploadAsync(bucket, key, lifetime, writeOptions, cancellationToken)` and
`CreatePresignedDownloadAsync(bucket, key, lifetime, cancellationToken)` return a URL, HTTP method,
required headers, and expiration for direct transfers. Check `SupportsPresignedRequests`: S3
supports this capability; file system and memory riders throw `NotSupportedException`.
Signing applies the same tenant-aware key prefix as ordinary storage operations. Authorize the
caller and select the bucket/key before signing; send the returned headers unchanged and configure
bucket CORS for browser clients. S3 lifetimes range from one second to seven days, and temporary
credentials can expire sooner. Presigned PUT uploads raw content; it is not a multipart/POST-policy API.

## Configuration Snippet

```json
{
  "ConnectionStrings": {
    "postgres": "Host=localhost;Database=myapp;Username=postgres;Password=postgres",
    "redis": "localhost:6379",
    "rabbitmq": "amqp://guest:guest@localhost:5672/"
  },
  "NOF": {
    "OidcServer": {
      "SigningKeyEncryptionKey": "replace-with-secret-configuration"
    }
  }
}
```
