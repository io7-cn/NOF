# Transport-independent application requests

[简体中文](../../zh-CN/design/application-requests.md) · [Architecture decision](0001-application-core-and-adapters.md)

ADR 0001 phase 1 adds a scoped executor. NOF.Application owns the contracts;
NOF.Infrastructure provides execution and DI registration. A plain ServiceCollection
can run use cases without a web server, serializer, database or broker.

Implement `IRequestHandler<TRequest,TResponse>` or inherit `RequestHandler<TRequest,TResponse>`.
`HandleAsync(request, context, cancellationToken)` returns `Task<TResponse>` without
an IRpcResponse constraint. DTOs, business unions and nullable values are supported.

```csharp
services.AddRequestHandler<GetOrderRequest, OrderDto?, GetOrder>();
var execution = await executor.ExecuteAsync<GetOrderRequest, OrderDto?>(request, context, ct);
switch (execution)
{
    case RequestCompleted<OrderDto?> completed:
        // Use completed.Response, including null or a business union.
        break;
    case RequestRejected rejected:
        // The adapter maps rejected.Kind and rejected.Failure.
        break;
}
```

Register each request/response pair once; this phase uses explicit registration,
not generated discovery. Callers create a DI scope per unit of work. Requests and
contexts must be non-null. RequestCompleted is an in-process execution case, not a
wire success envelope. A business Failure inside Result remains a completed execution.
Exceptions/cancellation propagate; uninitialized execution unions throw.

Register `IRequestMiddleware<TRequest,TResponse>` through DI, outermost first.
Its InvokeAsync can reject or call next with a normalized request/context. Handlers
are constructed at the terminal delegate after Context.Current is bound. Ambient
context is restored across awaits/errors. Middleware instances resolve before
invocation: defer tenant-specific dependencies/work until after normalization.

`AddNOFRequests()` installs only the executor: no automatic authentication,
authorization, validation, transactions, retries, idempotency or RPC attribute
interpretation. New entry points must establish trusted context and configure
required policies. Shared policy migration is ADR phase 2; existing RPC checks remain.

Native ASP.NET Core endpoints own binding, authorization, status, headers,
TypedResults, files and OpenAPI. Return the completed business value and map rejection
at that boundary. Do not serialize RequestExecution. Authentication rejections need
the host's challenge behavior. Native endpoints do not inherit RPC RequirePermission.

A generated RPC handler can bridge to the same plain-DTO use case:

```csharp
public sealed class ReadOrder(IRequestExecutor executor) : OrderServer.Read
{
    public override Task<Result<OrderDto>> HandleAsync(
        ReadOrderRequest request, Context context, CancellationToken cancellationToken)
        => ExecuteUseCaseAsync<OrderDto>(executor, request, context,
            static dto => Result.Success(dto), cancellationToken);
}
```

The bridge projects completed values and calls TResponse.CreateFailure for policy
rejections. An overload supports matching business/RPC response types. Existing RPC
transport security and wire contracts remain active.

The sample GetRootConfigNodesUseCase returns a plain DTO. The AotSmoke executable
runs one ReadOrderUseCase through generated RPC and native Minimal API with
source-generated JSON and native-AOT CI coverage.
