# Application services and generated operations

[简体中文](../../zh-CN/design/application-requests.md) · [Architecture decision](0001-application-core-and-adapters.md)

An application service operation is the use case. Keep the existing service contract,
generated `Service.Operation` base and override workflow; there is no second public
request-handler/executor hierarchy or a forwarding class per operation.

```csharp
public interface IOrderService : IApplicationService
{
    OrderDto? Find(FindOrderRequest request);
    Result<OrderDto> Save(SaveOrderRequest request);
}

public partial class OrderService : ApplicationService<IOrderService>;

public sealed class FindOrder(IOrderQueries orders) : OrderService.Find
{
    public override Task<OrderDto?> HandleAsync(
        FindOrderRequest request, Context context, CancellationToken ct)
        => orders.FindAsync(request.Id, context, ct);
}
```

Contracts declare one input and a synchronous business return type. Generated handlers
and clients are asynchronous and receive Context and CancellationToken. DTOs, nullable
values, ordinary business unions and optional Result responses are supported. Result
keeps its raw successful value; Success<T> remains an explicit optional message wrapper.
No IRpcResponse implementation is required by the service operation.

## Registration and invocation

```csharp
builder.AddApplicationPart(typeof(OrderService).Assembly);
builder.AddApplicationService<OrderService>();
builder.Services.AddScoped<IOrderServiceClient, LocalOrderServiceClient>();
```

The host registers a service container and selects its client adapter, as in the
previous service model. Application-part initialization automatically registers
operation implementations as scoped services. Do not register individual operations.
NOF hosts already add the calling assembly; add a separate Application assembly once.
There is no runtime handler-type scan or dynamic generic construction.

Call `IOrderServiceClient.FindAsync(request, context, ct)`. The generated local client
uses the shared inbound/outbound pipelines and creates an invocation scope. Identity
is the service contract plus operation name, so Read and Preview may share the same
request/response types while retaining different implementations and policy metadata.

## Adapters and execution semantics

A contract without a transport attribute is local by default and exposes no HTTP
routes. TransportOverMemory remains an explicit local-only declaration. Opt into
controller HTTP or JSON-RPC with TransportOverHttp; only HTTP adapters interpret
HttpEndpoint, binding, status codes, headers and wire serialization.

Native ASP.NET Core endpoints can inject a generated local client and return native
TypedResults, files or other host results. Business handlers need no IResult or
HttpContext dependency. Endpoint authorization remains the host's responsibility;
local invocation still runs NOF application authorization and tenant policies.

An explicit nullable response permits null; null from a non-nullable operation is an
execution error. A completed business Failure or business union remains a business
value. Pipeline policies use RequestInboundContext.Reject(failure), separately from
SetResponse(value). Handler resolution occurs after policies and normalized ambient
context binding. Caller-requested cancellation propagates rather than becoming failure.

The runtime-only ServiceExecution union distinguishes completed values (including
null) from policy rejection; it is never a wire envelope or a handler return contract.
Generated clients for ordinary responses report rejection as ServiceRejectedException.
Explicit outcome contracts retain their statically generated failure factory, preserving
Result failure behavior without reflection. Existing authorization, tenant isolation,
transactions/outbox and streaming pipelines remain in use.

## Breaking migration

| Previous API | Current API |
| --- | --- |
| IRpcService | IApplicationService |
| RpcServer<TService> | ApplicationService<TService> |
| RpcHandler<TRequest,TResponse> | generated Service.Operation base backed by ServiceOperation<TRequest,TResponse> |
| AddRpcServer<TService>() | AddApplicationService<TService>() |
| IRpcClient<TService> | IApplicationServiceClient<TService> |
| Local{ServiceName}Client | same generated name and operation call shape |
| Mandatory transport declaration | local by default; HTTP is explicit opt-in |
| Mandatory IRpcResponse return | declared DTO, nullable value or business union |

Remove prototype IRequestHandler/IRequestExecutor/AddRequestHandler and ExecuteUseCaseAsync
forwarders. Put the implementation back in the generated service-operation override.
For policies, replace the old Failure overload of SetResponse with explicit Reject.
Upgrade contracts, implementations, generated clients and host packages together.
The sample root configuration query now returns its DTO directly. The AOT smoke runs
DTOs, nullable returns, a business union without IRpcResponse, shared input/output
operations, rejection, generated HTTP, native HTTP and local-only services.
