# NOF.Hosting.AspNetCore

Native ASP.NET Core hosting, OpenAPI integration and HTTP adapters for NOF services.

## Endpoint modules

Organize feature routes in `IHttpEndpointModule.Map(IEndpointRouteBuilder)`. Use native
MapGet/MapPost, binding, TypedResults, filters and OpenAPI. Mark modules with
`[AutoInject(ServiceLifetime.Singleton)]` and add their assembly as an application part.
The host maps registered modules during initialization. A NOF host already adds its
calling assembly; service-operation implementations register through the Application
assembly. No per-operation host registration is needed.

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

Generated operation descriptors contain identity and nullability only. HttpContext
InvokeAsync executes HTTP authentication and application authorization/tenant policies
before resolving the handler. It does not call the local identity-copy path or bypass
the application pipeline. ServiceRejectionHttpFilter maps rejected execution to
ProblemDetails and preserves status/challenge headers. Feature modules map business
Failure cases to their chosen HTTP response.

Call app.MapOpenApi explicitly. Use native AddProblemDetails and error middleware in
the host. HttpRequestHeaderOptions selects headers admitted into the application context.

## Legacy RPC adapters

Optional HttpEndpoint, HttpVerb, TransportOverHttp, TransportOverMemory and generated
route metadata now belong to this package, not Contract. HTTP-specific generators and
diagnostics live in NOF.Hosting.SourceGenerator. Host-owned compatibility declarations
can keep controller/JSON-RPC/SSE adapters. The service contract package has no HTTP
annotations, and the sample uses native modules instead of annotated contracts.
Neither path generates clients or requires IRpcResponse.

See the [complete configuration REST example](../../../docs/en/design/configuration-rest.md)
and [migration guide](../../../docs/en/design/application-requests.md).

## License

Apache-2.0
