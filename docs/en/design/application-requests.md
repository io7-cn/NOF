# Application services and generated operations

[简体中文](../../zh-CN/design/application-requests.md) · [Architecture decision](0001-application-core-and-adapters.md)

An application service operation is the use case. Keep the proto-style service contract,
generated `Service.Operation` base and override workflow. Client generation is completely
removed: NOF generates no client interfaces, HTTP clients or local clients.

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

Contracts declare one input and a synchronous business return type. Context,
CancellationToken and Task belong to the generated server API. DTOs, nullable values,
business unions and optional Result responses are supported. `Success<T>` remains an
optional wrapper for successful message metadata; IRpcResponse is not required.

## Registration and invocation

```csharp
builder.AddApplicationPart(typeof(OrderService).Assembly);
builder.AddApplicationService<OrderService>();

var order = await OrderService.Find.InvokeAsync(services, request, context, ct);
```

Register the service container once. Application-part initialization registers operation
implementations as scoped services; no per-operation host registration is needed.
NOF hosts already add the calling assembly; add a separate Application assembly once.
Generated static InvokeAsync is server invocation glue, not a transport-selected client.
It resolves IApplicationServiceInvoker, creates a fresh invocation scope and executes
the inbound application policies. Context items flow directly; the caller's scoped
identity is cloned into the invocation scope. Local calls do not encode HTTP headers
or repeat bearer-token authentication; application authorization still runs. Do not resolve and call HandleAsync directly, which
would bypass these policies. Identity is service contract plus operation name; operations
may share request/response types while retaining different implementations and policies.

## HTTP adapters and callers

An undecorated contract exposes no automatic HTTP routes. TransportOverMemory remains
an explicit local-only declaration. Optional TransportOverHttp/HttpEndpoint metadata
continues to support the existing server HTTP adapter, including controller and JSON-RPC
routes, server binding, serialization metadata, SSE and OpenAPI. It emits no client code.

Native ASP.NET Core endpoints can call Service.Operation.InvokeAsync and return native
TypedResults, files or other host results. Handlers need no IResult or HttpContext.
Endpoint authorization belongs to the host; invocation also executes NOF application
authorization and tenant policies. A dedicated REST endpoint-module API remains future
work; the current implementation does not introduce a new endpoint DSL.

Remote callers maintain their own HTTP integration or use external tooling against the
HTTP API. A Contract reference no longer supplies a network proxy. The sample UI uses
application-owned ISampleApi/IOAuthDemoApi ports: server adapters call InvokeAsync; WASM
and MAUI adapters use explicit HttpClient calls. These adapters are sample code, not
framework-generated output. Their HTTP method, path, binding and codec choices are
maintained with the corresponding server endpoints.

## Execution semantics

An explicit nullable response permits null; a non-nullable operation returning null is
an execution error. Completed Failure/business union values remain business values.
Policies use RequestInboundContext.Reject(failure), separately from SetResponse(value).
Handler resolution follows policies and normalized context binding. Caller-requested
cancellation propagates rather than becoming failure.

ServiceExecution distinguishes completed values (including null) from policy rejection.
It is an invocation outcome, never a wire envelope or handler return contract. Static
InvokeAsync reports rejection as ServiceRejectedException for ordinary responses; explicit
IRpcResponse outcomes retain a statically emitted failure factory, preserving Result
behavior without reflection. Adapters needing explicit rejection mapping can inject
IApplicationServiceInvoker and match ServiceCompleted/ServiceRejected themselves.

Request/response outbound abstractions are also removed: IRequestOutboundPipelineExecutor,
RequestOutboundHandlerDelegate, IRequestOutboundMiddleware, RequestOutboundContext and
AddRequestOutboundMiddleware. Native HttpClient DelegatingHandler owns remote token/header
behavior and .NET HTTP tracing. The sample downstream handler reads per-call request
options rather than depending on a service contract. Command/Notification outbound
pipelines remain part of reliable messaging. HTTP ingress header filtering is configured
through HttpRequestHeaderOptions (renamed from HttpHeaderOutboundOptions).

## Breaking migration

| Previous API | Current API |
| --- | --- |
| IRpcService | IApplicationService |
| `RpcServer<TService>` | `ApplicationService<TService>` |
| `RpcHandler<TRequest,TResponse>` | generated Service.Operation / `ServiceOperation<TRequest,TResponse>` |
| `AddRpcServer<TService>`() | `AddApplicationService<TService>`() |
| `IRpcClient<TService>` / `IApplicationServiceClient<TService>` | removed, no replacement client marker |
| Generated I…ServiceClient / Http…ServiceClient | application-maintained HTTP integration |
| Generated Local…ServiceClient | Service.Operation.InvokeAsync(services, request, context, ct) |
| AddLocalApplicationServiceClient / GetRpcClient | removed; test `CallAsync<TResult>` accepts an invocation delegate |
| Mandatory transport declaration / IRpcResponse | optional HTTP metadata; plain, nullable or union business response |

Remove prototype IRequestHandler/IRequestExecutor/AddRequestHandler and forwarding classes.
Implement the generated operation override directly. Replace the old Failure overload of
SetResponse with Reject. Upgrade contracts, implementations, callers and host packages
together. Generated source files left by custom build tooling must be removed as well.
The AOT smoke covers DTOs, nullable returns, business unions, shared models, policy
rejection, server HTTP binding and native HTTP/local calls with no generated clients.
