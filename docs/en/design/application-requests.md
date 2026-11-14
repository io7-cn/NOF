# Application services and HTTP endpoint modules

[简体中文](../../zh-CN/design/application-requests.md) · [Configuration REST example](configuration-rest.md) · [Architecture decision](0001-application-core-and-adapters.md)

An application service operation is the use case. Keep the proto-style contract,
generated `Service.Operation` base and override workflow. Contracts contain business
requests/responses and application policy metadata, with no HTTP route or binding declarations.

```csharp
public interface IOrderService : IApplicationService
{
    OrderDto? Find(FindOrderRequest request);
    [FailureCase("Rejected")]
    OrderDto Save(SaveOrderRequest request);
}
public partial class OrderService : ApplicationService<IOrderService>;
public sealed class FindOrder(IOrderQueries orders) : OrderService.Find
{
    public override Task<OrderDto?> HandleAsync(
        FindOrderRequest request, InvocationContext context, CancellationToken ct)
        => orders.FindAsync(request.Id, context.Context, ct);
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
Register the service container once, not individual operations. Add each owning assembly
explicitly, including the host assembly when it contains endpoint modules. Operation identity is
contract plus operation name, so operations may share request/response models.

Local invocation creates a fresh scope, transfers Context items and clones the caller's
scoped identity. It runs authorization/tenant policies before handler resolution and
propagates caller cancellation. It does not serialize headers or reauthenticate tokens.
Do not directly resolve and invoke HandleAsync, which bypasses these policies.

## Native HTTP endpoint modules

Use ordinary MapFeatureEndpoints extensions returning native RouteGroupBuilder. Map operations as `group.MapGet<OrderService.Find>("/{id}")`; configure Request member/full construction and Response<TCase> independently. Application operations implement IServiceOperationDefinition with static Descriptor/Describe, preserving type safety and AOT metadata. A small Hosting generator supplies the concrete request lambda type without inspecting functions or consuming generated output. The host calls feature mapping explicitly; no endpoint-module DI registration or central handler list is needed.

Default DTO/Empty/null/failure responses are 200 JSON/204/200 JSON null/422 ProblemDetails. Configure specific failures by type for 404/409 etc. Context transfers only explicit semantic inputs and does not flow back from business. Runtime OpenAPI uses the final immutable binding, without executing user functions. Native middleware, exception handling and protocols remain ASP.NET Core responsibilities. See [HTTP adaptation](http-adaptation.md) and the [complete CRUD slice](configuration-rest.md).

## Execution and transport boundaries

Business handlers retain Task<TBusiness>; filters/next return ValueTask<object?> and complete InvokeAsync returns Task<object?>. Match the returned business union/value or generated filter branch. InvocationContext has no mutable outcome and expected failures do not use exceptions. Effective plans validate the permitted business and filter cases. See [invocation extensions](invocation-extensions.md).

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
| Generated Result failure projection on rejection | returned filter failure; explicit native HTTP mapping |
| String operation identity at native endpoints | generated Service.Operation.Descriptor |
| AddRequestHandler / separate executor hierarchy | generated operation override and application-part registration |

Upgrade contracts, implementations, adapters and hosts together. Return declared filter failure values instead of using SetResponse/Reject. Preserve Result Failure cases as
business data. Rebuild generated output; remove stale client files from custom build
tooling. Existing tags/releases remain immutable; rollback restores a coherent earlier
package/caller set.

[Declared expected failures](expected-failures.md) generate a closed result per operation; the declared method return describes success, while the Descriptor describes the actual execution response.

[Unified invocation extensions](invocation-extensions.md) replace the three inbound middleware families; hosts choose the engine. Message delivery prepares fixed metadata without an outbound extension chain.

[Current unified operation and message binding API](unified-operations.md).
