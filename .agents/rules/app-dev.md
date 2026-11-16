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
- Messaging: inbound void-returning ApplicationService operations use adapter-owned endpoints (RabbitMQ MapQueue only; Memory may expose subscriptions). Immediate calls use concrete clients. Application stages generic intents through IOutbox; infrastructure OutboxHandler<T> classes infer dispatch identity from T without MessageType/SchemaVersion declarations and AddApplicationPart registers their scoped adapters; AddOutbox() explicitly enables staging and workers. See docs/en/design/transactional-outbox.md.
- In-process events: `LocalEventHandler<T>`, `IEventPublisher`, `PublishAsEvent()`.
- Persistence: domain-owned aggregate repositories, Application DTO query ports and IUnitOfWork; explicit transactions use ITransactionManager/ITransaction. Core generic ORM/query contracts are removed; see docs/en/design/persistence-boundaries.md.
- Caching and mapping: `ICacheService`, `CacheKey<T>`, `IMapper`, `ProjectTo<TDestination>()`.
- Match the declared business success or the Application-generated concrete failure branches. A reply with no payload uses Empty; there is no shared generic Result. Success/Success<T> and WithMessage are removed; successful messages belong to explicit business DTOs. Do not use removed `IsSuccess`, `Extra` or reflective result projections. See `docs/en/design/result-unions.md`.
- Responses: plain business values, Empty, Optional<T>, or FailureCase-generated operation-specific Result unions. Generic Contract Result types are removed.

Message payloads are plain objects; consumer entry points are ordinary ApplicationService operations selected by host-owned bindings.

Client generation is completely removed: no client interfaces, HTTP clients or local clients. Keep proto-style synchronous contracts. Use generated Service.Operation.InvokeAsync on the server and application-maintained HTTP integrations remotely. HTTP routing uses ordinary MapFeatureEndpoints extensions and native groups, with MapGet<Service.Operation>/MapPost/etc. Registered operation descriptors preserve generic adapter metadata without imposing a static interface on user handlers; the Hosting analyzer supplies exact request lambda types. Request members merge individually; exact-type Response<T>(status) or complete TypedResults mappings replace as a whole (endpoint > nearest group > built-in); runtime OpenAPI uses the final immutable binding, without executing user functions. There is no endpoint-module DI discovery. See docs/en/design/http-adaptation.md. Legacy HTTP RPC/JSON-RPC adapters, HttpEndpoint/transport annotations, route generators and protocol readers are deleted without a compatibility layer. Explicit endpoint modules own all HTTP routes. Use native TypedResults.ServerSentEvents and platform SseParser for SSE.

All five inbound/outbound middleware families, delegates, outbound contexts and Add*Middleware APIs are removed. Application owns InvocationContext and typed business handlers; Infrastructure owns IInvocationFilter/InvocationDelegate/IInvocationPipeline and effective plans; AddInvocationFilter<T>(order) only declares scoped extensions. Hosts decide execution; NOF hosts select the optional Infrastructure InMemoryInvocationPipeline via UseInMemoryInvocationPipeline(). Filters resolve lazily under the current context, nested Global -> Service -> UseCase. Same-scope Use/Exclude and order writes use the last declaration; deeper scopes override outer rules; authorization/tenant checks precede handler construction. Outbox persists every JSON-scalar Context item and prepares complete immutable delivery Headers; fresh attempt scopes bind the restored Context before handler construction. Native clients forward OutboxDeliveryContext.Headers directly; MessageDelivery and its registrations are removed. Native ASP.NET Core owns HTTP middleware/endpoint filters/exception presentation; technical exceptions propagate to the host. Local invocation transfers Context and an isolated identity snapshot. FailureCase may annotate top-level nongeneric partial filters, generating sealed branches plus static FailureCases (no Filter.Result). Static Describe defaults to IncludeAll; Include<TFailure> selects only the owning catalog. Plans describe actual enabled filters without constructing handlers/filters. See docs/en/design/invocation-extensions.md.

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

builder.UseDbContext<AppDbContext>((creation, ef) => ef.UseNpgsql(
        DbConnectionStringTemplateResolver.ResolveTenantId(builder.Configuration.GetConnectionString("postgres")
            ?? throw new InvalidOperationException("Connection string 'postgres' not found."), creation.TenantId)))
    .ConfigureNOFDbContextOptions(nof => nof.TenantMode = TenantMode.DatabasePerTenant)
    .MigrateOnStart();

var app = builder.Build();
app.UseNOF();
app.MapOrderEndpoints();
app.MapOpenApi();
await app.RunAsync();
```

Add Redis and RabbitMQ through `builder.Services.AddCacheService().AddRedisCache(...)` and `builder.Services.AddRabbitMQ(...)`. `WebApplication.CreateBuilder(args).AddNOF()` registers execution only; optional cache, storage, messaging and Outbox are selected explicitly.

## Persistence Convention

Application code depends on aggregate repositories, DTO query ports and IUnitOfWork. The host owns `NOFDbContext`, EF model configuration, migrations, and the call to `UseDbContext<T>()`.

Deferred messages are staged in the current unit of work and become transactional only when the same context saves successfully:

```csharp
await outbox.EnqueueAsync(notification, context, cancellationToken: cancellationToken);
await unitOfWork.SaveChangesAsync(cancellationToken);
```

## Authentication

- Select native OpenIddict or an external OAuth/OIDC authority in the host; NOF supplies no OIDC protocol or storage package.
- Register native `AddAuthentication().AddJwtBearer(...)` with Authority/Audience and native middleware; NOF copies the verified HttpContext.User.
- Read identity through `IUserContext`; read the normalized current tenant from the handler's explicit `Context.TenantId`.
- The external identity provider owns its storage and lifecycle. The Sample AppHost runs Keycloak; configure native Cookie/OpenIdConnect/JwtBearer consumers in the application.

## Source Generators and Conventions

- Construct `IValueObject<T>` values through generated `From(...)` / `New(...)`; never use `default` or parameterless `new()` (`NOF018`).
- Use `[ValueObjectLength]` on string-backed value objects instead of duplicating maximum lengths in persistence mapping.
- Declare mappings on a `partial static` class with `[Mappable<TSource, TDestination>]`.
- Filter, order, and page before `ProjectTo<TDestination>()`; `NOF025` reports server-side shaping after projection.
- Add every assembly containing generated initializers with `AddApplicationPart(...)`.

Declared expected failures use Contract FailureCase(name), FailureCase(name, title), or FailureCase(name, title, typeof(CustomError)). The first argument is always a unique valid C# branch name; invalid declarations report NOF220. All cases implement Abstraction.IFailure with Title/Detail; custom cases also implement IFailure<TError> and expose Error. Match concrete types, never Title text or numeric codes. Service methods declare the successful type; generated Service.Operation.Result is used by operation overrides, Descriptor and InvokeAsync. Application operation bases own the Result and sealed failure cases, so handlers use Task<Result> and new Case(...). No CreateCase factories, shared Error wrapper, ExpectedOutcomeAttribute or NOF230/231/232 analyzer remain; native C# union switch-expression exhaustiveness checks apply. Ignoring results and catch-all arms follow ordinary C# semantics. OperationHandler, InvocationContext, descriptors and execution outcomes live in NOF.Application; semantic Context lives in NOF.Abstraction. Do not use the removed Domain FailureAttribute, static failure holders, failure.Throw or Failure-accepting exception constructors. Domain returns its own business values without depending on Contract. See docs/en/design/expected-failures.md.

Value objects implement static bool IsValid(T, out string? errorMessage) and optional Normalize. Generated From throws for invalid values; TryFrom returns false with default out for expected invalid input and never catches user-hook exceptions. Of and Validate are removed; legacy Validate or single-parameter IsValid is NOF019. Use TryFrom at application input boundaries to avoid validating twice. See docs/en/design/value-objects.md.

IsValid always has out string? errorMessage. Detailed TryFrom returns a non-null failure message and null on success; From uses the same text with ArgumentException. DomainException/DomainValidationException are removed. Application handlers forward TryFrom messages into FailureCase rather than duplicate wording.

Messaging: endpoints directly execute the mapped OperationHandler in isolated scopes through OperationExecutor, restoring Context before constructors and preserving authorization/filters. No Inbox/store/entity/receipt registry or consumer worker ships. Business owns idempotency, saves and transactions; native adapters own ACK/NACK, retry/dead-letter and concurrency. RabbitMQ ACK follows completed execution, not acceptance. Memory awaits handlers and propagates failures without process durability. MapQueue/AddMemoryMessaging never enable persistence. AddOutbox explicitly enables producer staging/workers; configure flat OutboxOptions and retain generated OutboxHandler<T> discovery. Resolve legacy Inbox Pending/Failed work with old binaries or explicit reviewed export/replay before guarded RemoveInbox; preserve required historical receipts via backup. See docs/en/design/transactional-outbox.md.

Domain collections: [ReadOnlyCollection] on private readonly List<T>/HashSet<T> fields in top-level non-generic partial classes generates a cached live IReadOnlyCollection view and CollectionBackingField metadata. OwnsMany field access is configured by EF Core after model configuration; explicit conflicts fail. Mut is removed. Use ReferenceEqualityComparer.Instance for entity HashSet navigation fields. NOF020 validates fields/names; NOF021 requests partial with a code fix.

Contract contains declarations and data models only and references Abstraction for shared permission models; it has no Application, Domain or Infrastructure dependency. Execution models/descriptors/outcomes/rejections live in NOF.Application. Context is NOF.Abstraction.Context; no Contract type forwarding remains. OperationDescriptor is the sole operation identity/input/output model; ServiceOperationReference/Descriptor.Reference and instance mapping/resolver fallbacks are removed. Registration requires static HandlerMappings without constructing containers. Contract generates no IServiceOperations.Method entries. OperationInspector checks descriptor CanSubmit; UseHostTenantAttribute and all host-tenant special cases are removed. Clients use native HTTP exceptions, not Application execution rejections. See docs/en/design/contract-boundaries.md.

Host composition: AddNOF registers execution only. Use AddCacheService().AddMemoryCacheService()/AddRedisCache(), AddObjectStorage().AddMemoryObjectStorage()/AddFileSystemObjectStorage()/AddAmazonS3ObjectStorage(), AddBackplane().AddMemoryBackplane()/AddRedisBackplane()/AddRabbitMQBackplane(), and AddIdGenerator().AddSnowflakeIdGenerator(). Snowflake implementation/options belong to Infrastructure; Domain has no AddNOFDomain registration. IdGenerator.Current returns IIdGenerator? and never throws for an absent binding; generated New() decides to throw when it requires a provider. HTTP copies only native HttpContext.User; select native JwtBearer and endpoint authorization in the host. No custom AuthenticationInvocationFilter/JWKS validation remains. JSON uses per-host IOptions<JsonSerializerOptions> through AddNOFJson, native HTTP JsonOptions and fresh standalone CreateNOF(); no global mutable options. Telemetry/exporters are native host choices. See docs/en/design/host-composition.md and its Chinese edition.

Authorization: declarations belong exclusively to Contract interfaces/operation methods, never handlers, request/message models or UI pages. Use RequireAuthenticated, nonempty RequirePermission(..., Match = PermissionMatch.All/Any), and AllowAnonymous; separate groups combine with AND, method declarations override interface defaults, inherited ambiguity fails configuration. NOF240/241/242 validate declarations; MetadataAttribute is removed. Abstraction owns immutable policies, PermissionMatch/IPermissionMetadata, IPermissionChecker and its default implementation, independent of Contract/Application/Domain/ASP.NET Core. Application owns AuthorizationPolicyResolver; Contract keeps declaration attributes and references Abstraction, while UI references only Abstraction. There is no standalone authorization package. AddNOF/AddNOFUI automatically install default checks through AddNOFAbstraction. AddPermissions, PermissionCatalog and PermissionDefinition are removed; application-owned directories are not enforcement whitelists. Grant providers evaluate current data on every check; cached plans contain declarations, never cached grants. Replace the grant provider, not the whole authorization flow, for application data. Execution and description share a cached Contract policy before handler construction; tenant validation precedes grant queries. UI checks identifiers/explicit UI Policy directly, without UseCase coupling; PermissionRouteView requires an explicit Policy, and stale async grants cannot overwrite newer decisions. Role/organization management and database schema changes are outside this capability. See docs/en/design/authorization.md and its Chinese edition.

Standalone operations: derive concrete classes directly from OperationHandler<TRequest> (one-way Task) or OperationHandler<TRequest,TResponse> (reply Task<TResponse>). AddApplicationPart automatically registers scoped handlers and typed descriptors, without partial or per-handler host registration. MapQueue<Handler> and HTTP MapGet/MapPost<Handler> use registry metadata and the descriptor visitor; IOperationDefinition is removed. Internal reactions need no Contract method. ContractType is nullable; only Contract declarations supply permissions. Global and UseCase<Handler>() filters plus host tenant/custom authorization rules still apply. The sample CountOrder is standalone.

JSON contexts: Context owns a JSON object with case-insensitive string keys and JSON string/finite-number/bool/null values only. Context.Items and item reads return detached snapshots; WithItem/WithItems/WithoutItem return new Context instances. WithItems accepts JsonObject; use GetValue<T> on values. Object keys, arbitrary CLR references and nested arrays/objects are removed. Outbox persists the entire scalar context in the reserved X-NOF-Context header, alongside stable ID, tenant, original trace and explicit Headers. Worker restoration/binding precedes handler construction; consumers receive restored JSON values automatically. Forward OutboxDeliveryContext.Headers directly; MessageDelivery and its registrations are deleted. No schema column or payload wrapper is added; old records retain only data originally saved. CacheServiceSelector.Configure is renamed ConfigureCacheServiceOptions; RabbitMQ registration/configuration chains directly on IServiceCollection. See docs/en/design/execution-context.md and its Chinese edition.

Execution models: Contract contains declarations and data models without generated code or partial interfaces. Application generates Result/failure cases from Contract FailureCase and owns OperationHandler/descriptors. IOperationExecutor.ExecuteAsync isolates local reply calls; host entries use the same concrete OperationExecutor.ExecuteInScopeAsync. InvocationContext composes Context; preserve authorization and filter ordering before handler construction. One-way void handlers return Task, never Result/FailureCase; Empty remains reply-producing. NOF220 rejects void FailureCase declarations.

Outbox handlers: concrete OutboxHandler<T> implementations need only HandleAsync. AddApplicationPart installs scoped typed delegates without constructing handlers or enabling workers. Identity is inferred from typeof(T).ToString(), excluding assembly versions; do not declare MessageType/SchemaVersion constants. NOF330 validates access/retired metadata, NOF331 rejects duplicate payload handlers. AddOutboxHandler<TMessage,THandler>() supports manually selected closed generic handlers. Payload renames and incompatible formats require reviewed durable-key/payload migrations; no dynamic CLR type loading, routing registry, ordering or automatic compatibility alias exists.

Persistence boundaries: native EF/NHibernate implement IOutboxStore with bounded owner-fenced claims and share the application's scoped unit of work. No cross-ORM facade or generic business memory ORM remains. Application-owned ports select native implementations. SaveChangesAsync translates native concurrency failures to System.Data.DBConcurrencyException and data-save failures to System.Data.DataException with the native cause; transaction failures, cancellation and unexpected errors propagate. Consumer handlers/filters explicitly save and commit; native projection upserts must include tenant columns and tenant-scoped unique keys. Sample Notifications uses a parameterized SQLite upsert; EnsureCreated never upgrades an existing receiver file.
