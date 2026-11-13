# Application services and HTTP endpoint modules

[简体中文](../../zh-CN/design/application-requests.md) · [Configuration REST example](configuration-rest.md) · [Architecture decision](0001-application-core-and-adapters.md)

An application service operation is the use case. Keep the proto-style contract,
generated `Service.Operation` base and override workflow. Contracts contain business
requests/responses and application policy metadata, with no HTTP route or binding declarations.

```csharp
public partial interface IOrderService : IApplicationService
{
    OrderDto? Find(FindOrderRequest request);
    [FailureCase("Rejected")]
    OrderDto Save(SaveOrderRequest request);
}
public partial class OrderService : ApplicationService<IOrderService>;
public sealed class FindOrder(IOrderQueries orders) : OrderService.Find
{
    public override Task<OrderDto?> HandleAsync(
        FindOrderRequest request, Context context, CancellationToken ct)
        => orders.FindAsync(request.Id, context, ct);
}
```

Each declaration has one reference-type input and a synchronous business return.
Context, CancellationToken and Task belong to the generated server API. DTOs, nullable
values and business unions are supported. IRpcResponse and generic Contract Result types
are removed. FailureCase generates a closed operation-specific Result inherited by its
handler. Empty represents payload-free success; successful messages belong to business DTOs.
No client interfaces, HTTP Clients or Local Clients are generated.

## Registration and local invocation

```csharp
builder.AddApplicationPart(typeof(OrderService).Assembly);
builder.AddApplicationService<OrderService>();
var order = await OrderService.Find.InvokeAsync(services, request, context, ct);
```

Application-part initialization registers operation implementations as scoped services.
Register the service container once, not individual operations. NOF hosts already add
the calling assembly; add a separate Application assembly once. Operation identity is
contract plus operation name, so operations may share request/response models.

Local invocation creates a fresh scope, transfers Context items and clones the caller's
scoped identity. It runs authorization/tenant policies before handler resolution and
propagates caller cancellation. It does not serialize headers or reauthenticate tokens.
Do not directly resolve and invoke HandleAsync, which bypasses these policies.

## Native HTTP endpoint modules

`IHttpEndpointModule` groups routes in the HTTP adapter. Use native MapGet/MapPost,
route/body/query binding, TypedResults, endpoint filters and OpenAPI metadata.
`[AutoInject(ServiceLifetime.Singleton)]` registers a module when its assembly is added
as an application part; host initialization calls Map once. A host needs no per-operation
registration or centralized collection of paths, bindings and status mappings.

```csharp
[AutoInject(ServiceLifetime.Singleton)]
public sealed class OrderEndpoints : IHttpEndpointModule
{
    public void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/orders/{id}", async (string id, HttpContext http, CancellationToken ct) =>
        {
            var order = await http.InvokeAsync(OrderService.Find.Descriptor, new FindOrderRequest(id), ct);
            return order is null ? Results.NotFound() : Results.Ok(order);
        }).AddEndpointFilter<ServiceRejectionHttpFilter>();
    }
}
```

Each generated operation exposes a typed `Descriptor` containing contract identity,
operation name and nullability, with no HTTP dependency. `HttpContext.InvokeAsync`
uses it to run HTTP authentication and the application inbound pipeline. It preserves
policy response headers and returns the declared business value. The descriptor avoids
string operation names or repeated request/response generic arguments at the endpoint.

`ServiceRejectionHttpFilter` maps execution rejection to native ProblemDetails, honoring
authorization status/challenge headers. Completed business failures remain business
values and are mapped by the feature module. Use ASP.NET Core AddProblemDetails and
status-code/exception middleware according to the host's error policy. HTTP authorization,
files, content negotiation and other web facilities remain native ASP.NET Core concerns.

For Native AOT, use endpoint methods with explicit response signatures when the service
container is generated in the same compilation. ASP.NET Core's request-delegate generator
cannot infer a lambda return type from another generator's not-yet-visible output.

The sample implements configuration CRUD, files, active-file selection, configuration
queries and SSE in ConfigNodeEndpoints. OAuth demo routes live in a separate module.
Its Contract project references no Hosting package and contains no HTTP annotations.
See the [complete REST example](configuration-rest.md) for requests, handlers, routes,
host registration and verification.

## Execution and transport boundaries

ServiceExecution distinguishes completed business values (including declared null)
from policy rejection. It is an invocation outcome, never a wire envelope or handler
return contract. All generated typed InvokeAsync entries report policy rejection as
ServiceRejectedException, including operations returning Result. No result-specific
failure factory is generated. Business Failure cases remain ordinary returned values.

Legacy HTTP RPC and JSON-RPC adapters are deleted, including HttpEndpoint, HttpVerb,
transport annotations, automatic route mapping, transport registration, dedicated result
writers and protocol readers. No compatibility layer remains. Contracts are always
transport independent; Contract's analyzer validates service signatures only. Declare
HTTP routes in endpoint modules and use native TypedResults and ProblemDetails.
For SSE use TypedResults.ServerSentEvents and the platform SseParser on the caller.
Result no longer defines CreateEmptyResponse.

Request/response outbound executor/delegate/context/middleware APIs and client generation
remain removed. Remote callers maintain HttpClient integrations and DelegatingHandler;
Invocation extensions and message delivery now follow the [host-selected execution model](invocation-extensions.md).

## Breaking migration

| Previous API | Current API |
| --- | --- |
| IRpcService / RpcServer / AddRpcServer | IApplicationService / ApplicationService / AddApplicationService |
| Generated clients and client marker | removed; explicit HTTP integration or server InvokeAsync |
| Contract HttpEndpoint / transport annotations | deleted; HTTP endpoint modules own explicit routes |
| IRpcResponse and CreateEmptyResponse | removed; business unions and explicit adapter conversion |
| Generated Result failure projection on rejection | ServiceRejectedException; HTTP filter maps policy rejection |
| String operation identity at native endpoints | generated Service.Operation.Descriptor |
| AddRequestHandler / separate executor hierarchy | generated operation override and application-part registration |

Upgrade contracts, implementations, adapters and hosts together. Replace SetResponse's
former failure overload with Reject for policies. Preserve Result Failure cases as
business data. Rebuild generated output; remove stale client files from custom build
tooling. Existing tags/releases remain immutable; rollback restores a coherent earlier
package/caller set.

[Declared expected failures](expected-failures.md) generate a closed result per operation; the declared method return describes success, while the Descriptor describes the actual execution response.

[Unified invocation extensions](invocation-extensions.md) replace the three inbound middleware families; hosts choose the engine. Message delivery prepares fixed metadata without an outbound extension chain.
