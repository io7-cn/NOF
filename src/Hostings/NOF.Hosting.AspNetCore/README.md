# NOF.Hosting.AspNetCore

Native ASP.NET Core operation binding, explicit feature mapping and runtime OpenAPI.

```csharp
var orders = app.MapGroup("/orders");
orders.MapGet<OrderService.Find>("/{id}")
    .Response<OrderService.Find.NotFound>(404);
```

Operation bases implement static IServiceOperationDefinition. Typed Request/Response
rules replace complete exact-type mappings on native groups/endpoints; no endpoint-module registration or
client generation is required. AddNOF registers JSON, ProblemDetails and OpenAPI services.
Call UseNOF, ordinary feature extensions, native health checks, MapOidcServer (when used)
and MapOpenApi explicitly. Native middleware and protocols remain host responsibilities.

Failure cases implement IFailure with Title/Detail, optionally typed Error. Map concrete
case types to HTTP statuses; never inspect display text. Context transfers explicit semantic
inputs only and does not flow back from business execution. Runtime execution and OpenAPI
read the same immutable binding without inspecting or executing user callbacks.

See the [English guide](../../../docs/en/design/http-adaptation.md),
[简体中文](../../../docs/zh-CN/design/http-adaptation.md) and
[complete CRUD example](../../../docs/en/design/configuration-rest.md).

## License

Apache-2.0
