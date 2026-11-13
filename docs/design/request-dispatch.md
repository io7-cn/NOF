# Request Dispatch

## Decision

Application operations use generated ApplicationService<TService> containers independently of transport; see [ADR 0001](../en/design/0001-application-core-and-adapters.md) and the [implementation guide](../en/design/application-requests.md). HTTP APIs use explicit endpoint modules; legacy RPC adapters are deleted.

## Current Invocation Model

- RPC operations: call `IApplicationService` methods through generated Service.Operation.InvokeAsync server entry points.
- Command sending: use `ICommandSender`.
- Notification publishing: use `INotificationPublisher`.
- Deferred dispatch: await `ICommandSender.DeferSendAsync(message, context, cancellationToken)` and `INotificationPublisher.DeferPublishAsync(message, context, cancellationToken)` to stage outbox messages before saving the same `IDbContext`. With an explicit transaction, durability requires `CommitAsync()` after `SaveChangesAsync()`.
- Streaming operations use `Result<IAsyncEnumerable<T>>` in the contract and `Task<Result<IAsyncEnumerable<T>>>` in server handlers and generated invocation entries.

## Notes

- HTTP exposure for RPC services is registered through `builder.AddApplicationService<TApplicationService>()` or `builder.Services.AddApplicationService<TApplicationService>()` and mapped automatically during application initialization.
- OpenAPI registration is always enabled by `WebApplication.CreateBuilder(args)`, but `app.MapOpenApi().AddNOF()` remains an explicit host decision.
- Application service operations directly implement business logic; native HTTP adapters call generated server operation entries and use ASP.NET Core response types.
- No client interfaces, HTTP Clients or Local Clients are generated. Remote integrations are maintained by their calling applications.
- ASP.NET Core hosting maps streaming RPC HTTP endpoints to .NET 11 server-sent events.
