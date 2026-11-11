---
trigger: always_on
---

# NOF Application Development Rules

Use this file when building applications on top of NOF.

## Architecture

```text
MyApp.Domain/      domain classes, value objects, failures, in-memory event payloads
MyApp.Contract/    RPC contracts, request/response models, commands, notifications, DTOs
MyApp.Application/ RPC servers and handlers, event handlers, mappings, cache keys
MyApp/             host, concrete persistence provider, Program.cs, appsettings
```

Typical dependency direction is `Host -> Application`, `Application -> Domain + Contract`, with the host selecting infrastructure and hosting packages.

## Core Abstractions

- Application services: declare IApplicationService contracts and partial ApplicationService<TService> containers; implement generated Service.Operation overrides. DTOs, nullable values and business unions need no IRpcResponse constraint. AddApplicationPart automatically registers scoped operation implementations; register containers with AddApplicationService<TService>(). Operation identity is service contract plus operation, so input/output models may be shared. See docs/en/design/application-requests.md.

- RPC: `IApplicationService`, `ApplicationService<TService>`, generated nested operation bases and static Service.Operation.InvokeAsync entries; no client interfaces or implementations are generated.
- Messaging: `CommandHandler<T>`, `NotificationHandler<T>`, `ICommandSender`, `INotificationPublisher`.
- In-process events: `InMemoryEventHandler<T>`, `IEventPublisher`, `PublishAsEvent()`.
- Persistence: `IDbContext`, `IDbContextFactory`, `IRepository<T>`, provider-neutral async LINQ extensions.
- Caching and mapping: `ICacheService`, `CacheKey<T>`, `IMapper`, `ProjectTo<TDestination>()`.
- Match the successful `T` itself or shared `NOF.Abstraction.Failure`. `Success<T>` is optional: use `Result<Success<T>>` only when successful message metadata is needed. Do not use removed `IsSuccess`, `Extra` or reflective result projections. See `docs/en/design/result-unions.md`.
- Results and request models: `Result`, `Result<T>`, `Result<IAsyncEnumerable<T>>`, `Optional<T>`.

Commands and notifications are plain payload objects; inheriting the corresponding handler base opts a handler into generated registration.

Client generation is completely removed: no client interfaces, HTTP clients or local clients. Keep proto-style synchronous contracts. Use generated Service.Operation.InvokeAsync on the server and application-maintained HTTP integrations remotely. Optional HTTP annotations generate server metadata only. Dedicated REST endpoint-module APIs remain future work.

Request/response outbound pipeline/context/middleware APIs are removed. Local invocation transfers Context and a cloned scoped identity directly into inbound policies; native HttpClient DelegatingHandler owns remote concerns. Command/Notification outbound messaging pipelines remain.

## Application Service Rules

- Every application service contract inherits `IApplicationService`. A transport declaration is optional; no declaration means local-only. Only `[TransportOverHttp(...)]` exposes HTTP routes.
- A contract method takes exactly one reference-type request and returns its declared non-task business value, including a DTO, nullable value or union. Do not put `CancellationToken` on the contract method or suffix it with `Async`.
- Controller-style HTTP methods use `[HttpEndpoint]`; JSON-RPC and memory contracts must not.
- A generated server container is a `partial` class inheriting `ApplicationService<TService>`.
- Register each server with `builder.AddApplicationService<TServer>()`; the host maps applicable transports during `BuildAsync()`.
- Generated server invocation and handler methods are asynchronous and receive `Context` explicitly.

## Program.cs Baseline

```csharp
using Microsoft.EntityFrameworkCore;
using NOF.Hosting;
using NOF.Hosting.AspNetCore;
using NOF.Infrastructure.EntityFrameworkCore;

var builder = NOFWebApplicationBuilder.Create(args);

builder.AddApplicationPart(typeof(OrderService).Assembly);
builder.AddApplicationService<OrderService>();

builder.UseDbContext<AppDbContext>()
    .WithTenantMode(TenantMode.DatabasePerTenant)
    .WithConnectionString(builder.Configuration.GetConnectionString("postgres")
        ?? throw new InvalidOperationException("Connection string 'postgres' not found."))
    .WithOptions(static (optionsBuilder, connectionString) => optionsBuilder.UseNpgsql(connectionString))
    .MigrateOnInitialize();

var app = await builder.BuildAsync();
app.MapOpenApi();
await app.RunAsync();
```

Add Redis and RabbitMQ through `builder.Services.AddRedisCache(...)` and `builder.Services.AddRabbitMQ(...)`. `NOFWebApplicationBuilder.Create(args)` already registers the default in-memory persistence, cache, command rider, and notification rider; explicit providers replace those defaults.

## Persistence Convention

Application code depends on `IDbContext` and `IRepository<T>`, not EF Core `DbContext`. The host owns `NOFDbContext`, EF model configuration, migrations, and the call to `UseDbContext<T>()`.

Deferred messages are added to the current `IDbContext` and become transactional only when the same context saves successfully:

```csharp
await notificationPublisher.DeferPublishAsync(notification, context, cancellationToken);
await dbContext.SaveChangesAsync(cancellationToken);
```

## Authentication

- Register a local OAuth/OIDC authority with `builder.AddOidcServer(...)` from `NOF.Hosting.AspNetCore.Extension.OidcServer`; its endpoints are mapped automatically.
- Register JWT validation with `builder.Services.AddAuthenticationResourceServer(...)`, configuring `AuthorizationServerIssuer`, `ExpectedIssuer`, and optional `Audience`.
- Read identity through `IUserContext`; read the normalized current tenant from the handler's explicit `Context.TenantId`.
- OIDC persistence uses the configured `IDbContext`, so configure a durable provider and migrations for production.

## Source Generators and Conventions

- Construct `IValueObject<T>` values through generated `Of(...)` / `New(...)`; never use `default` or parameterless `new()` (`NOF018`).
- Use `[ValueObjectLength]` on string-backed value objects instead of duplicating maximum lengths in persistence mapping.
- Declare mappings on a `partial static` class with `[Mappable<TSource, TDestination>]`.
- Filter, order, and page before `ProjectTo<TDestination>()`; `NOF025` reports server-side shaping after projection.
- Add every assembly containing generated initializers with `AddApplicationPart(...)`.
