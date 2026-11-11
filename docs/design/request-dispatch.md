# Request Dispatch

## Decision

Application use cases use IRequestHandler and IRequestExecutor independently of transport; see [ADR 0001](../en/design/0001-application-core-and-adapters.md) and the [implementation guide](../en/design/application-requests.md). RPC adapters continue to use IRpcService and RpcServer<TService>.

## Current Invocation Model

- RPC operations: call `IRpcService` methods through generated RPC clients or local server dispatch.
- Command sending: use `ICommandSender`.
- Notification publishing: use `INotificationPublisher`.
- Deferred dispatch: await `ICommandSender.DeferSendAsync(message, context, cancellationToken)` and `INotificationPublisher.DeferPublishAsync(message, context, cancellationToken)` to stage outbox messages before saving the same `IDbContext`. With an explicit transaction, durability requires `CommitAsync()` after `SaveChangesAsync()`.
- Streaming RPC operations use `Result<IAsyncEnumerable<T>>` at the contract layer, `Task<Result<IAsyncEnumerable<T>>>` at the client layer, and `IAsyncEnumerable<T>` inside application handlers.

## Notes

- HTTP exposure for RPC services is registered through `builder.AddRpcServer<TRpcServer>()` or `builder.Services.AddRpcServer<TRpcServer>()` and mapped automatically during application initialization.
- OpenAPI registration is always enabled by `NOFWebApplicationBuilder.Create(args)`, but `app.MapOpenApi()` remains an explicit host decision.
- RPC handlers may delegate to application use cases through ExecuteUseCaseAsync; native HTTP adapters use ASP.NET Core response types.
- `RpcServer<TService>` discovery generates `Local{RpcServerName}Client`; the client interface is selected through `IRpcClient<TService>` rather than a naming convention or marker attribute.
- ASP.NET Core hosting maps streaming RPC HTTP endpoints to .NET 11 server-sent events.
