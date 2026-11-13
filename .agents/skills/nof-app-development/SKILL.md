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

Client generation is completely removed: no client interfaces, HTTP clients or local clients. Generic Contract Result/Result<T> and their factories/classifier are removed; FailureCase generates operation-specific Result unions. Success/Success<T> and WithMessage are removed; successful messages belong to business DTOs. Keep proto-style synchronous contracts. Use generated Service.Operation.InvokeAsync on the server and application-maintained HTTP integrations remotely. HTTP routing belongs in IHttpEndpointModule using native ASP.NET Core binding/TypedResults and HttpContext.InvokeAsync(Service.Operation.Descriptor, request, ct). Modules use AutoInject(Singleton) plus application-part initialization. Legacy HTTP RPC/JSON-RPC adapters, HttpEndpoint/transport annotations, route generators and protocol readers are deleted without a compatibility layer. Explicit endpoint modules own all HTTP routes. Use native TypedResults.ServerSentEvents and platform SseParser for SSE.

All five inbound/outbound middleware families, delegates, outbound contexts and Add*Middleware APIs are removed. Application owns InvocationContext and typed business handlers; Infrastructure owns IInvocationFilter/InvocationDelegate/IInvocationPipeline and effective plans; AddInvocationFilter<T>(order) only declares scoped extensions. Hosts decide execution; NOF hosts select the optional Infrastructure InMemoryInvocationPipeline via UseInMemoryInvocationPipeline(). Filters resolve lazily under the current context, nested Global -> Service -> UseCase with stable per-scope order and Use/Exclude rules; authorization/tenant checks precede handler construction. Concrete clients use MessageDelivery for envelope metadata; generic Outbox restores allowlisted tenant/trace metadata in fresh attempt scopes. Native ASP.NET Core owns HTTP middleware/endpoint filters/exception presentation; technical exceptions propagate to the host. Local invocation transfers Context and an isolated identity snapshot. FailureCase may annotate top-level nongeneric partial filters, generating sealed branches plus static FailureCases (no Filter.Result). Static Describe defaults to IncludeAll; Include<TFailure> selects only the owning catalog. Plans describe actual enabled filters without constructing handlers/filters. See docs/en/design/invocation-extensions.md.

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
| `IOutbox` | `EnqueueAsync(message, context, options, ct)` | snapshot an intent in the current unit of work; infrastructure owns delivery |
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
| `[FailureCase("NotFound", "404")]` | Application generates a named message case and closed operation result from the Contract declaration; typeof(CustomError) selects custom data instead |
| `[Mappable<TSource, TDestination>]` | generates expression mapping registration on a `partial static` class |
| `IHttpEndpointModule` | maps native ASP.NET Core feature endpoints; use AutoInject(Singleton) for application-part registration |
| `Service.Operation.Descriptor` | identifies a typed operation for HTTP adapter invocation without transport metadata |
| `HttpContext.InvokeAsync` | runs HTTP authentication and application policies using an operation Descriptor |

## Decision Guide

| Goal | Use |
|---|---|
| expose REST HTTP | IHttpEndpointModule, native MapGet/MapPost/TypedResults, generated operation Descriptor |
| invoke operations in process | plain contracts and Service.Operation.InvokeAsync |
| persist application data | aggregate repositories / DTO query ports / IUnitOfWork; choose the provider in the host |
| cache data | `CacheKey<T>` + `ICacheService`; optionally replace the memory rider with Redis |
| send cross-boundary work | a concrete client, or IOutbox for transactional work |
| add an OAuth/OIDC server | `AddOidcServer(...)` |
| validate access tokens | `services.AddAuthenticationResourceServer(...)` |

## Conventions

- Add assemblies containing handlers, mappings, or `[AutoInject]` services through `AddApplicationPart(...)`.
- Register each service container through AddApplicationService<TServer>(); endpoint modules map explicitly through app.MapNOFEndpoints() after native Build(). Do not register individual operations.
- Build request contracts with one reference-type request and its declared non-task business return, including a DTO, nullable value or union.
- Keep persistence-provider APIs out of the application layer.
- Apply filtering, ordering, and paging before `ProjectTo<TDestination>()`.
- Keep explicit paths available when ambient `Mapper`, `IdGenerator`, or `EventPublisher` conveniences make dependencies unclear.

Declared expected failures use Contract FailureCase(name), FailureCase(name, errorCode), or FailureCase(name, typeof(CustomError)). The first argument is always a unique valid C# branch name; invalid declarations report NOF220. Default cases expose ErrorCode (defaulting to the name) and Message; custom cases expose typed Error without an imposed code or message. Service methods declare the successful type; generated Service.Operation.Result is used by operation overrides, Descriptor and InvokeAsync. Application operation bases own the Result and sealed failure cases, so handlers use Task<Result> and new Case(...). No CreateCase factories, shared Error wrapper, ExpectedOutcomeAttribute or NOF230/231/232 analyzer remain; native C# union switch-expression exhaustiveness checks apply. Ignoring results and catch-all arms follow ordinary C# semantics. ServiceOperation, InvocationContext, descriptors and execution outcomes live in NOF.Application; semantic Context lives in NOF.Abstraction. Do not use the removed Domain FailureAttribute, static failure holders, failure.Throw or Failure-accepting exception constructors. Domain returns its own business values without depending on Contract. See docs/en/design/expected-failures.md.

Value objects implement static bool IsValid(T, out string? errorMessage) and optional Normalize. Generated From throws for invalid values; TryFrom returns false with default out for expected invalid input and never catches user-hook exceptions. Of and Validate are removed; legacy Validate or single-parameter IsValid is NOF019. Use TryFrom at application input boundaries to avoid validating twice. See docs/en/design/value-objects.md.

IsValid always has out string? errorMessage. Detailed TryFrom returns a non-null failure message and null on success; From uses the same text with ArgumentException. DomainException/DomainValidationException are removed. Application handlers forward TryFrom messages into FailureCase rather than duplicate wording.

Unified operations: service implementation bases stay in ApplicationService. Contract generates no code; NOF.Contract.SourceGenerator retains its name and declaration analyzers only. Application owns operation identity metadata; immediate transport calls use concrete clients, and generic intents use IOutbox; plain interfaces and IApplicationServiceImplementation need no partial modifier. FailureCase remains on non-partial Contract methods; Application generates the concrete Result and cases. IOperationExecutor runs a selected operation in the current scope, IOperationInvoker isolates local calls, and all origins run the same policy boundary. Use invocation.Context for nested semantic APIs. RequestInboundContext, CommandInboundContext, NotificationInboundContext and InvocationKind are removed. HTTP response metadata stays in HTTP adapters. See docs/en/design/unified-operations.md.

Domain collections: [ReadOnlyCollection] on private readonly List<T>/HashSet<T> fields in top-level non-generic partial classes generates a cached live IReadOnlyCollection view and CollectionBackingField metadata. OwnsMany field access is configured by EF Core after model configuration; explicit conflicts fail. Mut is removed. Use ReferenceEqualityComparer.Instance for entity HashSet navigation fields. NOF020 validates fields/names; NOF021 requests partial with a code fix.

Contract contains declarations and data models only and has no NOF runtime dependency. Execution models/descriptors/outcomes/rejections live in NOF.Application. Context is NOF.Abstraction.Context; no Contract type forwarding remains. ServiceOperationReference is Application-owned identity/input data only; Contract no longer generates IServiceOperations.Method entries. ServiceOperationInspector in Application resolves and validates references, checks CanSubmit and reads RequiresHostTenant metadata; UseHostTenantAttribute.IsRequired is removed. Clients use native HTTP exceptions, not Application execution rejections. See docs/en/design/contract-boundaries.md.

Generic outbox: IMessageDispatcher and outbound RouteToQueue/Topic are removed. Register one stable message ID/version and IOutboxHandler<T> per type through AddOutbox. Concrete MemoryMessageClient/RabbitMQMessageClient coexist. Claims are owner-fenced and stream-aware; retries retain MessageId. Permanent failures block their stream. No user credentials are persisted. Drain old outbox and back up every tenant database before the explicit schema migration; do not automatically reinterpret legacy records. See docs/en/design/transactional-outbox.md.
