---
trigger: always_on
---

# NOF Framework Development Rules

Use this file when contributing to the NOF framework itself.

## Repository Layout

- `src/`: runtime packages, source generators, code fixes, hosting integrations, extensions, and providers.
- `src/Hostings/`: ASP.NET Core, Blazor WebAssembly and MAUI hosts.
- `src/Infrastructures/`: EF Core, NHibernate, RabbitMQ, and StackExchange.Redis providers.
- `sample/`: runnable multi-project sample and Aspire app host.
- `sample-tests/`: sample integration tests.
- `tests/NOF.*.Tests`: core, hosting, UI, and test-helper tests.
- `tests/Infrastructures/*`: provider-specific tests.
- `tests/Common/SourceGenerator`: shared generator test helpers.
- `docs/`: DocFX content; `website/`: homepage application.

## Tech Stack

- .NET 11 and C# 15 (`extension` blocks are used throughout the repository).
- Central Package Management through root `Directory.Packages.props`; `sample/Directory.Packages.props` imports it for sample-only versions.
- Roslyn incremental generators and analyzers targeting `netstandard2.0`.
- xUnit and Moq.

## Current Runtime Patterns

- Application services: declare IApplicationService contracts and partial ApplicationService<TService> containers; implement generated Service.Operation overrides. DTOs, nullable values and business unions are ordinary responses; IRpcResponse is removed. AddApplicationPart automatically registers scoped operation implementations; register containers with AddApplicationService<TService>(). Operation identity is service contract plus operation, so input/output models may be shared. See docs/en/design/application-requests.md.

- RPC: `IApplicationService`, `ApplicationService<TService>`, generated server operation entries, and `AddApplicationService<TServer>()`.
- Messaging: inbound Empty-returning ApplicationService operations use adapter-owned endpoints (RabbitMQ MapQueue only; Memory may expose subscriptions). Immediate calls use concrete clients. Application stages generic intents through IOutbox; infrastructure OutboxHandler<T> classes declare stable ID/version constants and AddApplicationPart registers their scoped adapters; AddOutbox() explicitly enables staging and workers. See docs/en/design/transactional-outbox.md.
- In-memory events: `LocalEventHandler<T>`, `IEventPublisher`, and optional `PublishAsEvent()` ambient convenience.
- Transactional outbox: await IOutbox.EnqueueAsync(message, context, headers, ct), then save the same IUnitOfWork and commit any explicit transaction. Snapshot at enqueue; no network call or save. Optional generic Headers are snapshotted; infrastructure owns ordering.
- Persistence: IUnitOfWork with Task-returning SaveChangesAsync; optional ITransactionManager/ITransaction with async begin/commit/rollback. Aggregate repository interfaces belong to the application domain, and DTO read ports to Application. Business-facing IDbContext/IRepository/IAsyncQueryable and infrastructure query/model/update facades are removed. The cross-ORM session/query/model/update layer is also removed. Native EF DbContext/NHibernate ISession stay inside provider stores and host adapters; application code uses owned ports and IUnitOfWork. Default memory persistence stores message state only, not arbitrary business entities. See docs/en/design/persistence-boundaries.md.
- Hosting: use native builders and synchronous Build. AddNOF registers defaults; Web hosts explicitly call UseNOF, ordinary MapFeatureEndpoints extensions and native health/identity mapping. IApplicationInitializationStep, topology, BuildNOFAsync, NOF host/builder wrappers, NOF.Hosting.Console, NOF.Hosting.SourceGenerator and NOF401 are removed. Database migration uses IHostedLifecycleService.StartingAsync via MigrateOnStart; OIDC client bootstrap is application-owned after migration. DeploymentOptions replaces IHostEnvironment extension state. Client AddNOF only registers UI services. See docs/en/design/native-hosting.md.
- Source generation: `[AutoInject]`, `[FailureCase]`, `[Mappable<TSource, TDestination>]`, `[NewableValueObject]`, `IValueObject<T>`, handler registration, and server operation/descriptor generation (no client generation).
- Application parts: `AddApplicationPart(assembly)` runs generated `AssemblyInitializeAttribute` initializers against the current `IServiceCollection`.
- Registries: `EventHandlerRegistry`, `MappingRegistry`, `ApplicationServiceRegistry`, and adapter-owned MemoryEndpointRegistry/RabbitMQBuilder are singleton instances stored in DI and freeze on first read. Do not reintroduce a process-wide type registry.
- Ambient helpers: `Mapper`, `IdGenerator`, and `EventPublisher` are bound/restored at managed entries or explicit services.BindNOF(context) scopes; ordinary DI scopes require no activation and NOF040/IDaemonService are removed; `Context.Current` follows the current async execution flow. Explicit APIs remain the primary runtime contracts.

Client generation is completely removed: no client interfaces, HTTP clients or local clients. Keep proto-style synchronous contracts. Use generated Service.Operation.InvokeAsync on the server and application-maintained HTTP integrations remotely. HTTP routing uses ordinary MapFeatureEndpoints extensions and native groups, with MapGet<Service.Operation>/MapPost/etc. Operation bases implement static IServiceOperationDefinition; the Hosting analyzer supplies exact request lambda types. Request members merge individually; exact-type Response<T>(status) or complete TypedResults mappings replace as a whole (endpoint > nearest group > built-in); runtime OpenAPI uses the final immutable binding, without executing user functions. There is no endpoint-module DI discovery. See docs/en/design/http-adaptation.md. Legacy HTTP RPC/JSON-RPC adapters, HttpEndpoint/transport annotations, route generators and protocol readers are deleted without a compatibility layer. Explicit endpoint modules own all HTTP routes. Use native TypedResults.ServerSentEvents and platform SseParser for SSE.

All five inbound/outbound middleware families, delegates, outbound contexts and Add*Middleware APIs are removed. Application owns InvocationContext and typed business handlers; Infrastructure owns IInvocationFilter/InvocationDelegate/IInvocationPipeline and effective plans; AddInvocationFilter<T>(order) only declares scoped extensions. Hosts decide execution; NOF hosts select the optional Infrastructure InMemoryInvocationPipeline via UseInMemoryInvocationPipeline(). Filters resolve lazily under the current context, nested Global -> Service -> UseCase. Same-scope Use/Exclude and order writes use the last declaration; deeper scopes override outer rules; authorization/tenant checks precede handler construction. Concrete clients use MessageDelivery for envelope metadata; generic Outbox restores allowlisted tenant/trace metadata in fresh attempt scopes. Native ASP.NET Core owns HTTP middleware/endpoint filters/exception presentation; technical exceptions propagate to the host. Local invocation transfers Context and an isolated identity snapshot. FailureCase may annotate top-level nongeneric partial filters, generating sealed branches plus static FailureCases (no Filter.Result). Static Describe defaults to IncludeAll; Include<TFailure> selects only the owning catalog. Plans describe actual enabled filters without constructing handlers/filters. See docs/en/design/invocation-extensions.md.

## Coding Rules

- Default classes to `public` throughout the repository. Use `internal` only when a compelling, concrete reason requires assembly-only visibility, and document that reason alongside the declaration. Being an implementation detail, a background service, or currently used only inside the repository is not sufficient justification.
- Use file-scoped namespaces, Allman braces, and braces for every control-flow body.
- Add XML documentation for public APIs under `src/`.
- Do not put NuGet versions in individual `.csproj` files.
- Register services directly through `IServiceCollection`; there is no service-registration-step pipeline.
- Preserve Native AOT annotations and analyzer isolation: a generator must not depend on another generator's output being visible as input.

## Build Commands

```bash
dotnet restore NOF.slnx -p:Configuration=Release
dotnet build NOF.slnx --configuration Release --no-restore
dotnet format --no-restore --verify-no-changes --verbosity diagnostic
dotnet test NOF.slnx --configuration Release --no-build --verbosity normal --collect:"XPlat Code Coverage"
```

Build before formatting so source-generator assemblies exist; set the Configuration environment variable to Release for format analysis. The GitHub matrix installs the MAUI workload on Windows before restore. See `workflows/nof-dev/run-ci-locally.md` for the exact sequence.

## Change Checklist

Before considering work done, verify all applicable items:

1. Put runtime and generator tests in the owning package test project; put provider tests under `tests/Infrastructures/` when a dedicated project is warranted.
2. Keep the sample and `sample-tests` compiling when public behavior changes.
3. Update public XML docs, `README.md`, `docs/`, and `.agents/` as applicable.
4. Add new projects to `NOF.slnx`; update CI/CD only when its discovery or build inputs require it.
5. Add dependency versions only to root `Directory.Packages.props`, or to `sample/Directory.Packages.props` for sample-only packages.
6. Run `dotnet format`, review any changes, then run the relevant builds and tests.

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
