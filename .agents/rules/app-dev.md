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
- Messaging: inbound Empty-returning ApplicationService operations use transport-owned MapQueue/MapSubscription endpoints. Immediate calls use concrete clients. Application stages generic intents through IOutbox; infrastructure AddOutbox registers stable IDs/versions and scoped IOutboxHandler<T> adapters. See docs/en/design/transactional-outbox.md.
- In-process events: `LocalEventHandler<T>`, `IEventPublisher`, `PublishAsEvent()`.
- Persistence: domain-owned aggregate repositories, Application DTO query ports and IUnitOfWork; explicit transactions use ITransactionManager/ITransaction. Core generic ORM/query contracts are removed; see docs/en/design/persistence-boundaries.md.
- Caching and mapping: `ICacheService`, `CacheKey<T>`, `IMapper`, `ProjectTo<TDestination>()`.
- Match the successful `T` itself or shared `NOF.Abstraction.Failure`. Payload-free Result is union(Empty, Failure). Success/Success<T> and WithMessage are removed; successful messages belong to explicit business DTOs. Do not use removed `IsSuccess`, `Extra` or reflective result projections. See `docs/en/design/result-unions.md`.
- Responses: plain business values, Empty, Optional<T>, or FailureCase-generated operation-specific Result unions. Generic Contract Result types are removed.

Message payloads are plain objects; consumer entry points are ordinary ApplicationService operations selected by host-owned bindings.

Client generation is completely removed: no client interfaces, HTTP clients or local clients. Keep proto-style synchronous contracts. Use generated Service.Operation.InvokeAsync on the server and application-maintained HTTP integrations remotely. HTTP routing uses ordinary MapFeatureEndpoints extensions and native groups, with MapGet<Service.Operation>/MapPost/etc. Operation bases implement static IServiceOperationDefinition; the Hosting analyzer supplies exact request lambda types. Request members merge individually; exact-type Response<T>(status) or complete TypedResults mappings replace as a whole (endpoint > nearest group > built-in); runtime OpenAPI uses the final immutable binding, without executing user functions. There is no endpoint-module DI discovery. See docs/en/design/http-adaptation.md. Legacy HTTP RPC/JSON-RPC adapters, HttpEndpoint/transport annotations, route generators and protocol readers are deleted without a compatibility layer. Explicit endpoint modules own all HTTP routes. Use native TypedResults.ServerSentEvents and platform SseParser for SSE.

All five inbound/outbound middleware families, delegates, outbound contexts and Add*Middleware APIs are removed. Application owns InvocationContext and typed business handlers; Infrastructure owns IInvocationFilter/InvocationDelegate/IInvocationPipeline and effective plans; AddInvocationFilter<T>(order) only declares scoped extensions. Hosts decide execution; NOF hosts select the optional Infrastructure InMemoryInvocationPipeline via UseInMemoryInvocationPipeline(). Filters resolve lazily under the current context, nested Global -> Service -> UseCase. Same-scope Use/Exclude and order writes use the last declaration; deeper scopes override outer rules; authorization/tenant checks precede handler construction. Concrete clients use MessageDelivery for envelope metadata; generic Outbox restores allowlisted tenant/trace metadata in fresh attempt scopes. Native ASP.NET Core owns HTTP middleware/endpoint filters/exception presentation; technical exceptions propagate to the host. Local invocation transfers Context and an isolated identity snapshot. FailureCase may annotate top-level nongeneric partial filters, generating sealed branches plus static FailureCases (no Filter.Result). Static Describe defaults to IncludeAll; Include<TFailure> selects only the owning catalog. Plans describe actual enabled filters without constructing handlers/filters. See docs/en/design/invocation-extensions.md.

## Application Service Rules

- Every application service contract inherits `IApplicationService`. Keep HTTP declarations out of Contract; expose HTTP explicitly through endpoint modules.
- A contract method takes exactly one reference-type request and returns its declared non-task business value, including a DTO, nullable value or union. Do not put `CancellationToken` on the contract method or suffix it with `Async`.
- Ordinary feature extensions own HTTP routes. MapGet<Service.Operation> preserves static type identity; typed Request/Response rules and runtime OpenAPI share the final binding.
- A generated server container is a `partial` class inheriting `ApplicationService<TService>`.
- Register each server with `builder.AddApplicationService<TServer>()`; ordinary HTTP feature extensions are explicitly called after Build, without DI module registration.
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
app.MapOrderEndpoints();
app.MapOpenApi();
await app.RunAsync();
```

Add Redis and RabbitMQ through `builder.Services.AddRedisCache(...)` and `builder.Services.AddRabbitMQ(...)`. `WebApplication.CreateBuilder(args).AddNOF()` already registers the default in-memory persistence, cache, command rider, and notification rider; explicit providers replace those defaults.

## Persistence Convention

Application code depends on aggregate repositories, DTO query ports and IUnitOfWork. The host owns `NOFDbContext`, EF model configuration, migrations, and the call to `UseDbContext<T>()`.

Deferred messages are staged in the current unit of work and become transactional only when the same context saves successfully:

```csharp
await outbox.EnqueueAsync(notification, context, cancellationToken: cancellationToken);
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

Declared expected failures use Contract FailureCase(name), FailureCase(name, title), or FailureCase(name, title, typeof(CustomError)). The first argument is always a unique valid C# branch name; invalid declarations report NOF220. All cases implement Abstraction.IFailure with Title/Detail; custom cases also implement IFailure<TError> and expose Error. Match concrete types, never Title text or numeric codes. Service methods declare the successful type; generated Service.Operation.Result is used by operation overrides, Descriptor and InvokeAsync. Application operation bases own the Result and sealed failure cases, so handlers use Task<Result> and new Case(...). No CreateCase factories, shared Error wrapper, ExpectedOutcomeAttribute or NOF230/231/232 analyzer remain; native C# union switch-expression exhaustiveness checks apply. Ignoring results and catch-all arms follow ordinary C# semantics. ServiceOperation, InvocationContext, descriptors and execution outcomes live in NOF.Application; semantic Context lives in NOF.Abstraction. Do not use the removed Domain FailureAttribute, static failure holders, failure.Throw or Failure-accepting exception constructors. Domain returns its own business values without depending on Contract. See docs/en/design/expected-failures.md.

Value objects implement static bool IsValid(T, out string? errorMessage) and optional Normalize. Generated From throws for invalid values; TryFrom returns false with default out for expected invalid input and never catches user-hook exceptions. Of and Validate are removed; legacy Validate or single-parameter IsValid is NOF019. Use TryFrom at application input boundaries to avoid validating twice. See docs/en/design/value-objects.md.

IsValid always has out string? errorMessage. Detailed TryFrom returns a non-null failure message and null on success; From uses the same text with ArgumentException. DomainException/DomainValidationException are removed. Application handlers forward TryFrom messages into FailureCase rather than duplicate wording.

Unified operations: service implementation bases stay in ApplicationService. Contract generates no code; NOF.Contract.SourceGenerator retains its name and declaration analyzers only. Application owns operation identity metadata; immediate transport calls use concrete clients, and generic intents use IOutbox; plain interfaces and IApplicationServiceImplementation need no partial modifier. FailureCase remains on non-partial Contract methods; Application generates the concrete Result and cases. IOperationExecutor runs a selected operation in the current scope, IOperationInvoker isolates local calls, and all origins run the same policy boundary. Use invocation.Context for nested semantic APIs. RequestInboundContext, CommandInboundContext, NotificationInboundContext and InvocationKind are removed. HTTP response metadata stays in HTTP adapters. See docs/en/design/unified-operations.md.

Domain collections: [ReadOnlyCollection] on private readonly List<T>/HashSet<T> fields in top-level non-generic partial classes generates a cached live IReadOnlyCollection view and CollectionBackingField metadata. OwnsMany field access is configured by EF Core after model configuration; explicit conflicts fail. Mut is removed. Use ReferenceEqualityComparer.Instance for entity HashSet navigation fields. NOF020 validates fields/names; NOF021 requests partial with a code fix.

Contract contains declarations and data models only and has no NOF runtime dependency. Execution models/descriptors/outcomes/rejections live in NOF.Application. Context is NOF.Abstraction.Context; no Contract type forwarding remains. ServiceOperationReference is Application-owned identity/input data only; Contract no longer generates IServiceOperations.Method entries. ServiceOperationInspector in Application resolves and validates references, checks CanSubmit and reads RequiresHostTenant metadata; UseHostTenantAttribute.IsRequired is removed. Clients use native HTTP exceptions, not Application execution rejections. See docs/en/design/contract-boundaries.md.

Generic outbox: IMessageDispatcher and outbound RouteToQueue/Topic are removed. Register one stable message ID/version and IOutboxHandler<T> per type through AddOutbox. Concrete MemoryMessageClient/RabbitMQMessageClient coexist. Claims are owner-fenced and stream-aware; retries retain MessageId. Permanent failures block their stream. No user credentials are persisted. Drain old outbox and back up every tenant database before the explicit schema migration; do not automatically reinterpret legacy records. See docs/en/design/transactional-outbox.md.

Ambient conveniences retain their explicit alternatives and PublishAsEvent timing. Managed HTTP/operation/message/test entries bind and restore; standalone code uses using var binding = services.BindNOF(context). Removed IDaemonService/ResolveDaemonServices and NOF040 impose no protocol on ordinary DI scopes. See docs/en/design/ambient-binding.md. AddNOF does not enable durable messaging: AddOutbox/AddInbox assemble the corresponding stores/models/workers idempotently. AddMemoryMessaging and AddRabbitMQ(options, endpoints) enable Inbox; client-only AddRabbitMQ installs no consumers. Transport-owned endpoints register scoped keyed IInboxHandler instances; the Inbox worker never interprets queue/topic/subscription structure. Old unprefixed receipt and order-state routes require an explicit offline migration to the selected memory:/rabbitmq: namespace; retain deduplication history. TransactionalMessageOptions.Outbox/Inbox.RunWorkers=false keeps staging/receipt APIs while stopping processing and cleanup; custom stores remain supported without an ORM.
