---
name: nof-app-development
description: Build .NET applications using the NOF (Neat Opinionated Framework) with clean architecture, RPC/CQRS messaging, provider-neutral persistence, source generators, caching, and OAuth/OIDC. Use when creating or changing a NOF app or when working with abstractions such as IApplicationService, ApplicationService<T>, CommandHandler<T>, NotificationHandler<T>, IDbContext, IRepository<T>, Result, or CacheKey<T>.
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

Application code should remain provider-neutral by using `IDbContext` and `IRepository<T>`; concrete EF Core or NHibernate types belong in the host.

## Application use cases

- Application services: declare IApplicationService contracts and partial ApplicationService<TService> containers; implement generated Service.Operation overrides. DTOs, nullable values and business unions are ordinary responses; IRpcResponse is removed. AddApplicationPart automatically registers scoped operation implementations; register containers with AddApplicationService<TService>(). Operation identity is service contract plus operation, so input/output models may be shared. See docs/en/design/application-requests.md.

Use native ASP.NET Core endpoints for HTTP binding, TypedResults, files and OpenAPI, calling Service.Operation.InvokeAsync(services, request, context, ct). There is no separate IRequestHandler/IRequestExecutor API or RPC forwarding class. ServiceExecution is an internal invocation outcome, not a wire envelope. Preserve application authorization and tenant policies across entry points.

Client generation is completely removed: no client interfaces, HTTP clients or local clients. Result is union(Empty, Failure); Result<T> is union(T, Failure). Success/Success<T> and WithMessage are removed; successful messages belong to business DTOs. Keep proto-style synchronous contracts. Use generated Service.Operation.InvokeAsync on the server and application-maintained HTTP integrations remotely. HTTP routing belongs in IHttpEndpointModule using native ASP.NET Core binding/TypedResults and HttpContext.InvokeAsync(Service.Operation.Descriptor, request, ct). Modules use AutoInject(Singleton) plus application-part initialization. Legacy HTTP RPC/JSON-RPC adapters, HttpEndpoint/transport annotations, route generators and protocol readers are deleted without a compatibility layer. Explicit endpoint modules own all HTTP routes. Use native TypedResults.ServerSentEvents and platform SseParser for SSE.

Request/response outbound pipeline/context/middleware APIs are removed. Local invocation transfers Context and a cloned scoped identity directly into inbound policies; native HttpClient DelegatingHandler owns remote concerns. Command/Notification outbound messaging pipelines remain.

## Message and RPC Types

| Concern | Declaration | Handler / registration |
|---|---|---|
| RPC | `IApplicationService` | generated nested base under `ApplicationService<TService>`; register with `AddApplicationService<TServer>()` |
| Command | plain payload | `CommandHandler<T>` |
| Notification | plain payload | `NotificationHandler<T>` |
| In-memory event | arbitrary payload | `InMemoryEventHandler<T>` |

All generated RPC handlers, command handlers, notification handlers, and in-memory event handlers receive `Context` plus `CancellationToken`.

## Dispatch APIs

| Interface | Method | Use |
|---|---|---|
| Generated server operation | `Service.Operation.InvokeAsync(services, request, context, ct)` | policy-controlled in-process invocation; remote callers own HTTP integration |
| `ICommandSender` | `SendAsync(command, context, ct)` | immediate command dispatch |
| `ICommandSender` | `DeferSendAsync(command, context, ct)` | stage a command in the current outbox |
| `ICommandSender` | `DeferSendOrderedAsync(command, orderKey, context, ..., ct)` | stage ordered command delivery |
| `INotificationPublisher` | `PublishAsync(notification, context, ct)` | immediate fan-out |
| `INotificationPublisher` | `DeferPublishAsync(notification, context, ct)` | stage a notification in the current outbox |
| `INotificationPublisher` | `DeferPublishOrderedAsync(notification, orderKey, context, ..., ct)` | stage ordered notification delivery |
| `IEventPublisher` | `PublishAsync(payload, context, ct)` | current-scope in-memory event dispatch |

Save the same `IDbContext` after a deferred dispatch to commit the application changes and outbox record together.

`PublishAsEvent()` uses the ambient publisher and current handler context. Prefer an injected `IEventPublisher` and its explicit async API when asynchronous control or test visibility matters.

## Source Generator Surface

| Attribute / interface | Behavior |
|---|---|
| `IValueObject<T>` | generates validated `Of(...)`, explicit primitive cast, equality, JSON conversion, and initialization checks |
| `[NewableValueObject]` | for `IValueObject<long>` only; generates `New()` and `New(IIdGenerator)` |
| `[ValueObjectLength(max, MinimumLength = ...)]` | validates string length and supplies persistence maximum-length metadata |
| `[AutoInject(ServiceLifetime...)]` | emits DI descriptors through an assembly initializer |
| `[Failure(...)]` | generates static `Failure` members |
| `[Mappable<TSource, TDestination>]` | generates expression mapping registration on a `partial static` class |
| `IHttpEndpointModule` | maps native ASP.NET Core feature endpoints; use AutoInject(Singleton) for application-part registration |
| `Service.Operation.Descriptor` | identifies a typed operation for HTTP adapter invocation without transport metadata |
| `HttpContext.InvokeAsync` | runs HTTP authentication and application policies using an operation Descriptor |

## Decision Guide

| Goal | Use |
|---|---|
| expose REST HTTP | IHttpEndpointModule, native MapGet/MapPost/TypedResults, generated operation Descriptor |
| invoke operations in process | plain contracts and Service.Operation.InvokeAsync |
| persist application data | `IDbContext` / `IRepository<T>`; choose EF Core or NHibernate in the host |
| cache data | `CacheKey<T>` + `ICacheService`; optionally replace the memory rider with Redis |
| send cross-boundary work | `ICommandSender` / `INotificationPublisher`, passing the current `Context` |
| add an OAuth/OIDC server | `AddOidcServer(...)` |
| validate access tokens | `services.AddAuthenticationResourceServer(...)` |

## Conventions

- Add assemblies containing handlers, mappings, or `[AutoInject]` services through `AddApplicationPart(...)`.
- Register each service container through AddApplicationService<TServer>(); endpoint modules map at BuildAsync(). Do not register individual operations.
- Build request contracts with one reference-type request and its declared non-task business return, including a DTO, nullable value or union.
- Keep persistence-provider APIs out of the application layer.
- Apply filtering, ordering, and paging before `ProjectTo<TDestination>()`.
- Keep explicit paths available when ambient `Mapper`, `IdGenerator`, or `EventPublisher` conveniences make dependencies unclear.
