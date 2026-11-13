---
trigger: always_on
---

# NOF Application Development Rules

Use this file when building applications on top of NOF.

## Architecture

```text
MyApp.Domain/      domain classes, value objects, failures, in-memory event payloads
MyApp.Contract/    application service contracts, request/response models, commands, notifications, DTOs
MyApp.Application/ application services and handlers, event handlers, mappings, cache keys
MyApp/             host, concrete persistence provider, Program.cs, appsettings
```

Typical dependency direction is `Host -> Application`, `Application -> Domain + Contract`, with the host selecting infrastructure and hosting packages.

## Core Abstractions

- Application services: declare IApplicationService contracts and partial ApplicationService<TService> containers; implement generated Service.Operation overrides. DTOs, nullable values and business unions are ordinary responses; IRpcResponse is removed. AddApplicationPart automatically registers scoped operation implementations; register containers with AddApplicationService<TService>(). Operation identity is service contract plus operation, so input/output models may be shared. See docs/en/design/application-requests.md.

- RPC: `IApplicationService`, `ApplicationService<TService>`, generated nested operation bases and static Service.Operation.InvokeAsync entries; no client interfaces or implementations are generated.
- Messaging: Empty-returning ApplicationService operations, host-owned Queue/Subscription bindings and IMessageDispatcher. Pass invocation.Context to semantic dispatch APIs.
- In-process events: `InMemoryEventHandler<T>`, `IEventPublisher`, `PublishAsEvent()`.
- Persistence: domain-owned aggregate repositories, Application DTO query ports and IUnitOfWork; explicit transactions use ITransactionManager/ITransaction. Core generic ORM/query contracts are removed; see docs/en/design/persistence-boundaries.md.
- Caching and mapping: `ICacheService`, `CacheKey<T>`, `IMapper`, `ProjectTo<TDestination>()`.
- Match the successful `T` itself or shared `NOF.Abstraction.Failure`. Payload-free Result is union(Empty, Failure). Success/Success<T> and WithMessage are removed; successful messages belong to explicit business DTOs. Do not use removed `IsSuccess`, `Extra` or reflective result projections. See `docs/en/design/result-unions.md`.
- Responses: plain business values, Empty, Optional<T>, or FailureCase-generated operation-specific Result unions. Generic Contract Result types are removed.

Message payloads are plain objects; consumer entry points are ordinary ApplicationService operations selected by host-owned bindings.

Client generation is completely removed: no client interfaces, HTTP clients or local clients. Keep proto-style synchronous contracts. Use generated Service.Operation.InvokeAsync on the server and application-maintained HTTP integrations remotely. HTTP routing belongs in IHttpEndpointModule using native ASP.NET Core binding/TypedResults and HttpContext.InvokeAsync(Service.Operation.Descriptor, request, ct). Modules use AutoInject(Singleton) plus application-part initialization. Legacy HTTP RPC/JSON-RPC adapters, HttpEndpoint/transport annotations, route generators and protocol readers are deleted without a compatibility layer. Explicit endpoint modules own all HTTP routes. Use native TypedResults.ServerSentEvents and platform SseParser for SSE.

All five inbound/outbound middleware families, delegates, outbound contexts and Add*Middleware APIs are removed. Application defines IInvocationFilter/InvocationContext/InvocationDelegate and IInvocationPipeline; AddInvocationFilter<T>(order) only declares scoped extensions. Hosts decide execution; NOF hosts select the optional Infrastructure InMemoryInvocationPipeline via UseInMemoryInvocationPipeline(). Filters resolve lazily in ascending stable order under the current context; authorization/tenant checks precede handler construction. MessageDelivery directly prepares ID, tenant and producer trace metadata for immediate and outbox sends. Native ASP.NET Core owns HTTP middleware/endpoint filters/exception presentation; technical exceptions propagate to the host. Local invocation transfers Context and an isolated identity snapshot. See docs/en/design/invocation-extensions.md.

## Application Service Rules

- Every application service contract inherits `IApplicationService`. Keep HTTP declarations out of Contract; expose HTTP explicitly through endpoint modules.
- A contract method takes exactly one reference-type request and returns its declared non-task business value, including a DTO, nullable value or union. Do not put `CancellationToken` on the contract method or suffix it with `Async`.
- IHttpEndpointModule owns HTTP routing/binding/results; HttpContext.InvokeAsync uses the generated Descriptor and the HTTP application-policy boundary.
- A generated server container is a `partial` class inheriting `ApplicationService<TService>`.
- Register each server with `builder.AddApplicationService<TServer>()`; native HTTP modules are registered through AutoInject(Singleton) and mapped explicitly by app.MapNOFEndpoints() after native Build().
- Generated server invocation and handler methods are asynchronous and receive `Context` explicitly.

## Program.cs Baseline

```csharp
using Microsoft.EntityFrameworkCore;
using NOF.Hosting;
using NOF.Hosting.AspNetCore;
using NOF.Infrastructure.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args).AddNOF();

builder.AddApplicationPart(typeof(OrderService).Assembly);
builder.AddApplicationService<OrderService>();

builder.UseDbContext<AppDbContext>()
    .WithTenantMode(TenantMode.DatabasePerTenant)
    .WithConnectionString(builder.Configuration.GetConnectionString("postgres")
        ?? throw new InvalidOperationException("Connection string 'postgres' not found."))
    .WithOptions(static (optionsBuilder, connectionString) => optionsBuilder.UseNpgsql(connectionString))
    .MigrateOnStart();

var app = builder.Build();
app.UseNOF();
app.MapNOFEndpoints();
app.MapOpenApi();
await app.RunAsync();
```

Add Redis and RabbitMQ through `builder.Services.AddRedisCache(...)` and `builder.Services.AddRabbitMQ(...)`. `WebApplication.CreateBuilder(args).AddNOF()` already registers the default in-memory persistence, cache, command rider, and notification rider; explicit providers replace those defaults.

## Persistence Convention

Application code depends on aggregate repositories, DTO query ports and IUnitOfWork. The host owns `NOFDbContext`, EF model configuration, migrations, and the call to `UseDbContext<T>()`.

Deferred messages are staged in the current unit of work and become transactional only when the same context saves successfully:

```csharp
await notificationPublisher.DeferPublishAsync(notification, context, cancellationToken);
await unitOfWork.SaveChangesAsync(cancellationToken);
```

## Authentication

- Register a local OAuth/OIDC authority with `builder.AddOidcServer(...)` from `NOF.Hosting.AspNetCore.Extension.OidcServer`; its endpoints are mapped automatically.
- Register JWT validation with `builder.Services.AddAuthenticationResourceServer(...)`, configuring `AuthorizationServerIssuer`, `ExpectedIssuer`, and optional `Audience`.
- Read identity through `IUserContext`; read the normalized current tenant from the handler's explicit `Context.TenantId`.
- OIDC persistence uses the configured infrastructure session, so configure a durable provider and migrations for production.

## Source Generators and Conventions

- Construct `IValueObject<T>` values through generated `From(...)` / `New(...)`; never use `default` or parameterless `new()` (`NOF018`).
- Use `[ValueObjectLength]` on string-backed value objects instead of duplicating maximum lengths in persistence mapping.
- Declare mappings on a `partial static` class with `[Mappable<TSource, TDestination>]`.
- Filter, order, and page before `ProjectTo<TDestination>()`; `NOF025` reports server-side shaping after projection.
- Add every assembly containing generated initializers with `AddApplicationPart(...)`.

Declared expected failures use Contract FailureCase(name), FailureCase(name, errorCode), or FailureCase(name, typeof(CustomError)). The first argument is always a unique valid C# branch name; invalid declarations report NOF220. Default cases expose ErrorCode (defaulting to the name) and Message; custom cases expose typed Error without an imposed code or message. Service methods declare the successful type; generated Service.Operation.Result is used by operation overrides, Descriptor and InvokeAsync. Application operation bases own the Result and sealed failure cases, so handlers use Task<Result> and new Case(...). No CreateCase factories, shared Error wrapper, ExpectedOutcomeAttribute or NOF230/231/232 analyzer remain; native C# union switch-expression exhaustiveness checks apply. Ignoring results and catch-all arms follow ordinary C# semantics. ServiceOperation abstractions live in NOF.Contract. Do not use the removed Domain FailureAttribute, static failure holders, failure.Throw or Failure-accepting exception constructors. Domain returns its own business values without depending on Contract. See docs/en/design/expected-failures.md.

Value objects implement static bool IsValid(T, out string? errorMessage) and optional Normalize. Generated From throws for invalid values; TryFrom returns false with default out for expected invalid input and never catches user-hook exceptions. Of and Validate are removed; legacy Validate or single-parameter IsValid is NOF019. Use TryFrom at application input boundaries to avoid validating twice. See docs/en/design/value-objects.md.

IsValid always has out string? errorMessage. Detailed TryFrom returns a non-null failure message and null on success; From uses the same text with ArgumentException. DomainException/DomainValidationException are removed. Application handlers forward TryFrom messages into FailureCase rather than duplicate wording.

Unified operations: service implementation bases stay in ApplicationService. Contract independently generates IServiceOperations.Method input-only references for producers; plain interfaces and IApplicationServiceImplementation need no partial modifier. FailureCase remains on non-partial Contract methods; Application generates the concrete Result and cases. IOperationExecutor runs a selected operation in the current scope, IOperationInvoker isolates local calls, and all origins run the same policy boundary. Use invocation.Context for nested semantic APIs. RequestInboundContext, CommandInboundContext, NotificationInboundContext and InvocationKind are removed. HTTP response metadata stays in HTTP adapters. See docs/en/design/unified-operations.md.

Domain collections: [ReadOnlyCollection] on private readonly List<T>/HashSet<T> fields in top-level non-generic partial classes generates a cached live IReadOnlyCollection view and CollectionBackingField metadata. OwnsMany field access is configured by EF Core after model configuration; explicit conflicts fail. Mut is removed. Use ReferenceEqualityComparer.Instance for entity HashSet navigation fields. NOF020 validates fields/names; NOF021 requests partial with a code fix.
