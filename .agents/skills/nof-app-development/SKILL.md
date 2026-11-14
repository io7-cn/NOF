---
name: nof-app-development
description: Build .NET applications using the NOF (Neat Opinionated Framework) with clean architecture, RPC/CQRS messaging, provider-neutral persistence, source generators, caching, and OAuth/OIDC. Use when creating or changing a NOF app or when working with abstractions such as IApplicationService, ApplicationService<T>, IOutbox, InvocationContext, IUnitOfWork, aggregate repositories, Result, or CacheKey<T>.
---

# NOF Application Development

Read `../../rules/app-dev.md` first. Use `references/recipes.md` for application code shapes and `references/infrastructure.md` for provider and authentication setup.

## Architecture

```text
MyApp.Domain/      domain types, value objects, failures, in-memory event payloads
MyApp.Contract/    application service contracts, requests/responses, commands, notifications, DTOs
MyApp.Application/ application services and handlers, message/event handlers, mappings, cache keys
MyApp/             host and concrete persistence/transport configuration
```

Application code depends on domain-owned aggregate repositories, Application DTO query ports and IUnitOfWork. Do not expose IQueryable, ICollection repositories or tracking operations. Concrete persistence belongs in host/infrastructure implementations. See docs/en/design/persistence-boundaries.md.

## Application use cases

- Application services: declare IApplicationService contracts and partial ApplicationService<TService> containers; implement generated Service.Operation overrides. DTOs, nullable values and business unions are ordinary responses; IRpcResponse is removed. AddApplicationPart automatically registers scoped operation implementations; register containers with AddApplicationService<TService>(). Operation identity is service contract plus operation, so input/output models may be shared. See docs/en/design/application-requests.md.

Use native ASP.NET Core endpoints for HTTP binding, TypedResults, files and OpenAPI, calling Service.Operation.InvokeAsync(services, request, context, ct). There is no separate IRequestHandler/IRequestExecutor API or RPC forwarding class. Business handlers retain Task<TBusiness>; filters/next return ValueTask<object?> and complete InvokeAsync returns Task<object?>. Match business values/unions or effective filter failures; no ServiceExecution/ServiceRejectedException or context result slots remain. Preserve application authorization and tenant policies across entry points.

Client generation is completely removed: no client interfaces, HTTP clients or local clients. Generic Contract Result/Result<T> and their factories/classifier are removed; FailureCase generates operation-specific Result unions. Success/Success<T> and WithMessage are removed; successful messages belong to business DTOs. Keep proto-style synchronous contracts. Use generated Service.Operation.InvokeAsync on the server and application-maintained HTTP integrations remotely. HTTP routing uses ordinary MapFeatureEndpoints extensions and native groups, with MapGet<Service.Operation>/MapPost/etc. Operation bases implement static IServiceOperationDefinition; the Hosting analyzer supplies exact request lambda types. Request members merge individually; exact-type Response<T>(status) or complete TypedResults mappings replace as a whole (endpoint > nearest group > built-in); runtime OpenAPI uses the final immutable binding, without executing user functions. There is no endpoint-module DI discovery. See docs/en/design/http-adaptation.md. Legacy HTTP RPC/JSON-RPC adapters, HttpEndpoint/transport annotations, route generators and protocol readers are deleted without a compatibility layer. Explicit endpoint modules own all HTTP routes. Use native TypedResults.ServerSentEvents and platform SseParser for SSE.

All five inbound/outbound middleware families, delegates, outbound contexts and Add*Middleware APIs are removed. Application owns InvocationContext and typed business handlers; Infrastructure owns IInvocationFilter/InvocationDelegate/IInvocationPipeline and effective plans; AddInvocationFilter<T>(order) only declares scoped extensions. Hosts decide execution; NOF hosts select the optional Infrastructure InMemoryInvocationPipeline via UseInMemoryInvocationPipeline(). Filters resolve lazily under the current context, nested Global -> Service -> UseCase. Same-scope Use/Exclude and order writes use the last declaration; deeper scopes override outer rules; authorization/tenant checks precede handler construction. Concrete clients use MessageDelivery for envelope metadata; generic Outbox restores allowlisted tenant/trace metadata in fresh attempt scopes. Native ASP.NET Core owns HTTP middleware/endpoint filters/exception presentation; technical exceptions propagate to the host. Local invocation transfers Context and an isolated identity snapshot. FailureCase may annotate top-level nongeneric partial filters, generating sealed branches plus static FailureCases (no Filter.Result). Static Describe defaults to IncludeAll; Include<TFailure> selects only the owning catalog. Plans describe actual enabled filters without constructing handlers/filters. See docs/en/design/invocation-extensions.md.

## Message and RPC Types

| Concern | Declaration | Handler / registration |
|---|---|---|
| RPC | `IApplicationService` | generated nested base under `ApplicationService<TService>`; register with `AddApplicationService<TServer>()` |
| Queued work / subscribed fact | plain payload + IApplicationService method returning Empty | generated Service.Operation + host binding |
| In-memory event | arbitrary payload | `LocalEventHandler<T>` |

Generated service operations receive InvocationContext plus CancellationToken; in-memory events and dispatch receive semantic Context. InvocationContext composes Context; it does not inherit it.

## Dispatch APIs

| Interface | Method | Use |
|---|---|---|
| Generated server operation | `Service.Operation.InvokeAsync(services, request, context, ct)` | policy-controlled in-process invocation; remote callers own HTTP integration |
| `IOutbox` | `EnqueueAsync(message, context, headers, ct)` | snapshot an intent in the current unit of work; infrastructure owns delivery |
| `IEventPublisher` | `PublishAsync(payload, context, ct)` | current-scope in-memory event dispatch |

Save the same scoped `IUnitOfWork` after a deferred dispatch to commit the application changes and outbox record together.

`PublishAsEvent()` uses the ambient publisher and current handler context. Prefer an injected `IEventPublisher` and its explicit async API when asynchronous control or test visibility matters.

## Source Generator Surface

| Attribute / interface | Behavior |
|---|---|
| `IValueObject<T>` | generates `From(...)` / `TryFrom(...)` using Normalize and IsValid, explicit primitive cast, equality, JSON conversion, and initialization checks |
| `[NewableValueObject]` | for `IValueObject<long>` only; generates `New()` and `New(IIdGenerator)` |
| `[ValueObjectLength(max, MinimumLength = ...)]` | validates string length and supplies persistence maximum-length metadata |
| `[AutoInject(ServiceLifetime...)]` | emits DI descriptors through an assembly initializer |
| `[FailureCase("NotFound", "Node not found")]` | Application generates a closed result; `(name, title, typeof(CustomError))` adds typed Error. Every case has Title/Detail |
| `[Mappable<TSource, TDestination>]` | generates expression mapping registration on a `partial static` class |
| `MapFeatureEndpoints` | ordinary static extension returning RouteGroupBuilder; host explicitly calls it, without DI module discovery |
| `Service.Operation.Descriptor` | identifies a typed operation for HTTP adapter invocation without transport metadata |
| `HttpContext.InvokeAsync` | runs HTTP authentication and application policies using an operation Descriptor |

## Decision Guide

| Goal | Use |
|---|---|
| expose REST HTTP | ordinary feature extension, native groups, MapGet<Service.Operation>, Request/Response rules |
| invoke operations in process | plain contracts and Service.Operation.InvokeAsync |
| persist application data | aggregate repositories / DTO query ports / IUnitOfWork; choose the provider in the host |
| cache data | `CacheKey<T>` + `ICacheService`; optionally replace the memory rider with Redis |
| send cross-boundary work | a concrete client, or IOutbox for transactional work |
| add an OAuth/OIDC server | native OpenIddict or an external identity provider |
| validate HTTP access tokens | native AddAuthentication().AddJwtBearer(...) |

## Conventions

- Add assemblies containing handlers, mappings, or `[AutoInject]` services through `AddApplicationPart(...)`.
- Register each service container through AddApplicationService<TServer>(); ordinary feature extensions map explicitly after native Build(). Do not register individual operations.
- Build request contracts with one reference-type request and its declared non-task business return, including a DTO, nullable value or union.
- Keep persistence-provider APIs out of the application layer.
- Apply filtering, ordering, and paging before `ProjectTo<TDestination>()`.
- Keep explicit paths available when ambient `Mapper`, `IdGenerator`, or `EventPublisher` conveniences make dependencies unclear.

Declared expected failures use Contract FailureCase(name), FailureCase(name, title), or FailureCase(name, title, typeof(CustomError)). The first argument is always a unique valid C# branch name; invalid declarations report NOF220. All cases implement Abstraction.IFailure with Title/Detail; custom cases also implement IFailure<TError> and expose Error. Match concrete types, never Title text or numeric codes. Service methods declare the successful type; generated Service.Operation.Result is used by operation overrides, Descriptor and InvokeAsync. Application operation bases own the Result and sealed failure cases, so handlers use Task<Result> and new Case(...). No CreateCase factories, shared Error wrapper, ExpectedOutcomeAttribute or NOF230/231/232 analyzer remain; native C# union switch-expression exhaustiveness checks apply. Ignoring results and catch-all arms follow ordinary C# semantics. ServiceOperation, InvocationContext, descriptors and execution outcomes live in NOF.Application; semantic Context lives in NOF.Abstraction. Do not use the removed Domain FailureAttribute, static failure holders, failure.Throw or Failure-accepting exception constructors. Domain returns its own business values without depending on Contract. See docs/en/design/expected-failures.md.

Value objects implement static bool IsValid(T, out string? errorMessage) and optional Normalize. Generated From throws for invalid values; TryFrom returns false with default out for expected invalid input and never catches user-hook exceptions. Of and Validate are removed; legacy Validate or single-parameter IsValid is NOF019. Use TryFrom at application input boundaries to avoid validating twice. See docs/en/design/value-objects.md.

IsValid always has out string? errorMessage. Detailed TryFrom returns a non-null failure message and null on success; From uses the same text with ArgumentException. DomainException/DomainValidationException are removed. Application handlers forward TryFrom messages into FailureCase rather than duplicate wording.

Unified operations: service implementation bases stay in ApplicationService. Contract generates no code; NOF.Contract.SourceGenerator retains its name and declaration analyzers only. Application owns operation identity metadata; immediate transport calls use concrete clients, and generic intents use IOutbox; plain interfaces and IApplicationServiceImplementation need no partial modifier. FailureCase remains on non-partial Contract methods; Application generates the concrete Result and cases. IOperationExecutor runs a selected operation in the current scope, IOperationInvoker isolates local calls, and all origins run the same policy boundary. Use invocation.Context for nested semantic APIs. RequestInboundContext, CommandInboundContext, NotificationInboundContext and InvocationKind are removed. HTTP response metadata stays in HTTP adapters. See docs/en/design/unified-operations.md.

Domain collections: [ReadOnlyCollection] on private readonly List<T>/HashSet<T> fields in top-level non-generic partial classes generates a cached live IReadOnlyCollection view and CollectionBackingField metadata. OwnsMany field access is configured by EF Core after model configuration; explicit conflicts fail. Mut is removed. Use ReferenceEqualityComparer.Instance for entity HashSet navigation fields. NOF020 validates fields/names; NOF021 requests partial with a code fix.

Contract contains declarations and data models only and has no NOF runtime dependency. Execution models/descriptors/outcomes/rejections live in NOF.Application. Context is NOF.Abstraction.Context; no Contract type forwarding remains. ServiceOperationDescriptor is the sole operation identity/input/output model; ServiceOperationReference/Descriptor.Reference and instance mapping/resolver fallbacks are removed. Registration requires static HandlerMappings without constructing containers. Contract generates no IServiceOperations.Method entries. ServiceOperationInspector checks descriptor CanSubmit; UseHostTenantAttribute and all host-tenant special cases are removed. Clients use native HTTP exceptions, not Application execution rejections. See docs/en/design/contract-boundaries.md.

Generic outbox: IMessageDispatcher and outbound RouteToQueue/Topic are removed. Each concrete OutboxHandler<T> declares public const string MessageType and optional public const int SchemaVersion (default 1). AddApplicationPart applies generated scoped handler/schema registrations without constructing handlers or enabling workers; AddOutbox() explicitly activates the capability. No Attribute or partial is needed. NOF330 validates declarations; NOF331 rejects same-assembly ambiguity. Split multi-message handlers and preserve existing persisted IDs/versions; OutboxBuilder is removed. Explicit AddOutboxHandler<TMessage,THandler>(id, version) supports manually selected closed adapters; open generic templates are not auto-discovered. MemoryMessageClient remains available; RabbitMQ publishing uses native IChannel APIs in infrastructure. Claims are owner-fenced; retries retain MessageId and custom header snapshots. No send/receive ordering, stream allocation, gap waiting or failure blocking remains. Infrastructure owns ordering when needed. Enqueue accepts IReadOnlyDictionary<string,string?> headers; OutboxDeliveryContext exposes read-only Headers. NOF-managed message ID/tenant/trace keys are reserved. Arbitrary Context.Items and user identity are not copied; callers own explicitly persisted headers. Drain old outbox and back up every tenant database before the explicit schema migration; do not automatically reinterpret legacy records. Apply the explicit RemoveMessageOrdering migration after draining or explicitly migrating ordered Inbox/Outbox work; retain deduplication history. Custom store CleanupAsync returns only a message count. See docs/en/design/transactional-outbox.md.

Ambient conveniences retain their explicit alternatives and PublishAsEvent timing. Managed HTTP/operation/message/test entries bind and restore; standalone code uses using var binding = services.BindNOF(context). Removed IDaemonService/ResolveDaemonServices and NOF040 impose no protocol on ordinary DI scopes. See docs/en/design/ambient-binding.md. AddNOF does not enable durable messaging: AddOutbox/AddInbox assemble the corresponding stores/models/workers idempotently. AddMemoryMessaging and ConfigureRabbitMQ queue mappings enable Inbox; native topology-only configuration does not. AddRabbitMQ configures native ConnectionFactory separately; repeatable fluent ConfigureRabbitMQ appends Topology callbacks and queue mappings before Build. Framework StartingAsync runs all topology before consumers without user lifecycle classes; client-only AddRabbitMQ installs no consumers. Transport-owned endpoints register scoped keyed IInboxHandler instances; the Inbox worker never interprets queue/topic/subscription structure. Old unprefixed receipt routes require an explicit offline migration to the selected memory:/rabbitmq: namespace; retain deduplication history. TransactionalMessageOptions.Outbox/Inbox.RunWorkers=false keeps staging/receipt APIs while stopping processing and cleanup; custom stores remain supported without an ORM.

Native persistence: IInboxStore/IOutboxStore have feature-specific EF and NHibernate implementations, using native bulk DML and owner fencing. No IPersistenceSession/factory/transaction, IEntitySet, IProviderQuery/executor, UpdateSetters, general memory ORM, IDbModelBuilder or provider-neutral model contributor remains. EF model extensions implement IEFCoreModelConfiguration with native ModelBuilder; NH uses Action<ModelMapper>. OIDC protocol and storage packages are removed; hosts directly select OpenIddict or an external identity provider. Sample uses its existing ConfigurationDbContext and migration chain for native OpenIddict models. AddInMemoryMessagingPersistence supplies only scoped transactional intents/receipts. Business persistence tests select native SQLite or business-owned test ports. InboxMessageType and MemoryCommandRider/MemoryNotificationRider are removed; MemoryMessageClient directly writes opaque queue/subscription receipts. Existing databases need the explicit RemoveInboxClassification migration after previous migrations, preserving receipt IDs/routes/history.

Host composition: AddNOF registers execution only. Use AddCacheService().AddMemoryCacheService()/AddRedisCache(), AddObjectStorage().AddMemoryObjectStorage()/AddFileSystemObjectStorage()/AddAmazonS3ObjectStorage(), AddBackplane().AddMemoryBackplane()/AddRedisBackplane()/AddRabbitMQBackplane(), and AddIdGenerator().AddSnowflakeIdGenerator(). Snowflake implementation/options belong to Infrastructure; Domain has no AddNOFDomain registration. IdGenerator.Current returns IIdGenerator? and never throws for an absent binding; generated New() decides to throw when it requires a provider. HTTP copies only native HttpContext.User; select native JwtBearer and endpoint authorization in the host. No custom AuthenticationInvocationFilter/JWKS validation remains. JSON uses per-host IOptions<JsonSerializerOptions> through AddNOFJson, native HTTP JsonOptions and fresh standalone CreateNOF(); no global mutable options. Telemetry/exporters are native host choices. See docs/en/design/host-composition.md and its Chinese edition.
