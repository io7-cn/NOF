# Configuration node REST implementation

[简体中文](../../zh-CN/design/configuration-rest.md) · [Application services](application-requests.md)

The runnable sample keeps business declarations in NOF.Sample.Contract, existing
generated operation overrides in NOF.Sample.Application, and HTTP adapters in
NOF.Sample/Endpoints. The host contains service-container/provider setup; feature
modules own routes, binding, status codes and response schemas.

## Contract and handlers

```csharp
public interface INOFSampleService : IApplicationService
{
    Result<CreatedConfigNode> CreateConfigNode(CreateConfigNodeRequest request);
    Result<GetConfigNodeByIdResponse> GetConfigNodeById(GetConfigNodeByIdRequest request);
    GetRootConfigNodesResponse GetRootConfigNodes(GetRootConfigNodesRequest request);
    Result UpdateConfigNodeParent(UpdateConfigNodeParentRequest request);
    Result DeleteConfigNode(DeleteConfigNodeRequest request);
    // File, children, configuration and stream operations remain ordinary business declarations.
}
public sealed record CreatedConfigNode(long Id);
public partial class NOFSampleService : ApplicationService<INOFSampleService>;
```

Create validates name/parent, saves the aggregate, updates cache state and returns
CreatedConfigNode with the persisted ID. Move rejects a missing parent, self-parenting
and descendant cycles. Delete rejects a node with children. Existing domain events and
projection handlers remain active. None of these handlers references ASP.NET Core.

## HTTP module

ConfigNodeEndpoints implements IHttpEndpointModule and uses the existing AutoInject
assembly-initializer mechanism. Its route table is:

| HTTP | Path | Bound input | Successful response |
| --- | --- | --- | --- |
| POST | /api/config-nodes | body: name, optional parentId | 201 CreatedConfigNode; Location points to GET |
| GET | /api/config-nodes | none | 200 array of root ConfigNodeDto |
| GET | /api/config-nodes/{id} | path id | 200 ConfigNodeDto |
| GET | /api/config-nodes/{id}/children | path id | 200 children projection |
| PATCH | /api/config-nodes/{id}/parent | path id; body parentId, explicitly nullable | 204 |
| DELETE | /api/config-nodes/{id} | path id | 204 |
| PUT | /api/config-nodes/{id}/files/{fileName} | path identity; body content | 204 |
| DELETE | /api/config-nodes/{id}/files/{fileName} | path identity | 204 |
| PUT | /api/config-nodes/{id}/active-file | path id; body fileName, explicitly nullable | 204 |
| GET | /api/configuration | query appName | 200 configuration |
| GET | /api/sample-events | query topic/count/intervalMilliseconds/forceFailure | SSE |

HTTP body types are adapter-owned and distinct from business requests. For example,
MoveConfigNodeBody contains only parentId. The endpoint constructs the business request
from path id plus body parentId, so a supplied nodeId body property cannot change the
target. Omitting parentId is invalid; explicitly passing null moves the node to root.

The actual move entry uses native ASP.NET Core binding and results:

```csharp
nodes.MapPatch("/{id:long}/parent", MoveAsync);

public static async Task<Results<NoContent, ProblemHttpResult>> MoveAsync(
    long id, [FromBody] MoveConfigNodeBody body, HttpContext http, CancellationToken ct)
    => ToCommandResult(await http.InvokeAsync(NOFSampleService.UpdateConfigNodeParent.Descriptor,
        new UpdateConfigNodeParentRequest { NodeId = id, NewParentId = body.ParentId }, ct));
```

Business errors become application/problem+json with detail and code: missing node is
404; missing parent is 422; name conflicts, cycles and deleting a parent with children
are 409. Input validation is 400. Policy rejection uses ServiceRejectionHttpFilter and
retains 401/403 challenge information separately from business errors.

```http
HTTP/1.1 201 Created
Location: /api/config-nodes/123
Content-Type: application/json

{"id":123}
```

```http
HTTP/1.1 409 Conflict
Content-Type: application/problem+json

{"type":"https://tools.ietf.org/html/rfc9110#section-15.5.10","title":"Conflict","status":409,"detail":"Node with same name already exists.","code":"config_node.name_conflict"}
```

## Host and consumers

```csharp
builder.AddApplicationPart(typeof(NOFSampleService).Assembly);
builder.AddApplicationService<NOFSampleService>();
// NOF hosts already add their calling assembly, including AutoInject endpoint modules.
builder.Services.Configure<CacheServiceOptions>(options => options.KeyPrefix = "nof-sample:{tenantId}:");
builder.Services.AddProblemDetails();
var app = await builder.BuildAsync();
app.UseStatusCodePages();
app.MapOpenApi();
```

The existing host selects PostgreSQL database-per-tenant, Redis and RabbitMQ. No CRUD
operation is registered separately. A module in a separate HTTP assembly needs one
AddApplicationPart for that assembly. Tenant-specific cache prefixes prevent a cached
resource from crossing database-per-tenant boundaries.

The shared UI port remains application-owned. ServerSampleApi calls local operations;
HttpSampleApi explicitly consumes native resource bodies, 201/204 and ProblemDetails.
WASM/MAUI no longer call the former /rpc/NOFSample paths. OAuth demo endpoints use their
own module while preserving their existing URLs.

## Executable sources and verification

- [Contract](https://github.com/io7-cn/NOF/blob/main/sample/NOF.Sample.Contract/INOFSampleService.cs)
- [Create handler](https://github.com/io7-cn/NOF/blob/main/sample/NOF.Sample.Application/RequestHandlers/CreateConfigNode.cs)
- [Complete endpoint module and HTTP bodies](https://github.com/io7-cn/NOF/blob/main/sample/NOF.Sample/Endpoints/ConfigNodeEndpoints.cs)
- [Host registration](https://github.com/io7-cn/NOF/blob/main/sample/NOF.Sample/Program.cs)
- [UI HTTP adapter](https://github.com/io7-cn/NOF/blob/main/sample/NOF.Sample.UI/Services/HttpSampleApi.cs)
- [HTTP integration tests](https://github.com/io7-cn/NOF/blob/main/sample-tests/NOF.Sample.Tests/ConfigNodeRestTests.cs)

Tests exercise real handlers and persistence, Location follow-up, hierarchy projections,
file CRUD, conflicts/cycles, malformed body binding, cross-tenant reads with SQLite,
the actual UI adapter, policy rejection before handler construction, OpenAPI and absence
of old implicit RPC routes. Run the sample test project and the framework's Native AOT
smoke when changing the HTTP bridge.
