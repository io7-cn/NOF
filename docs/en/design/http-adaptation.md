# Native HTTP adaptation

[简体中文](../../zh-CN/design/http-adaptation.md) · [Complete CRUD](configuration-rest.md) · [Ambient binding](ambient-binding.md)

Contract contains business declarations and data. Application generates operation identity/bases/results. Hosts use native groups and ordinary MapFeatureEndpoints extensions; AddNOF supplies HTTP runtime services, while UseNOF binds request-scoped conveniences. Routes, health checks, OpenAPI and OIDC mapping are explicit host choices. There is no module discovery or client generation.

## Typed operations and requests

MapGet/MapPost/MapPut/MapPatch/MapDelete<Service.Operation> select IServiceOperationDefinition through static Descriptor/Describe, with no handler construction or runtime generic activation. The small Hosting analyzer retains exact Request lambda types from original declarations/referenced metadata; it never consumes another generator's output. Descriptor overloads remain available for authored catalogs. NativeBuilder exposes a real RouteHandlerBuilder for native-only extensions.

Input priority is explicit Member, same-name Route, then Query for GET/HEAD/DELETE or JSON body for POST/PUT/PATCH. Header sources are explicit and source descriptions use HttpInputLocation. Route/query comparisons default to OrdinalIgnoreCase. Binding honors host JSON names/converters, required/init/constructor members, defaults and unknown-member handling. Arrays/lists accept repeated values; scalar duplicates fail. Composite input is deserialized once; a body id never overwrites route identity. OpenAPI projects only actual body members. Malformed input yields 400/415 before handler construction; technical user-hook exceptions propagate.

Member(...).FromRoute/FromQuery/FromHeader/FromBody overrides one field. Complete Route/Query/Header/Body chains end in Map (up to three sources, one whole body). Body() binds the entire JSON request, including scalars/collections. Custom supplies its explicit input/body/media contract. ByConvention resets complete construction. User callbacks are never executed to discover input contracts.

## Complete response mappings

```csharp
var nodes = app.MapGroup("/api/config-nodes");
nodes.MapGet<NOFSampleService.GetConfigNodeById>("/{id:long}")
    .WithName("GetConfigNode")
    .Response((GetConfigNodeByIdResponse value) => TypedResults.Ok(value.Node))
    .Response<NOFSampleService.GetConfigNodeById.NotFound>(404);
nodes.MapPost<NOFSampleService.CreateConfigNode>("")
    .Response((CreatedConfigNode value) => TypedResults.CreatedAtRoute(
        value: value, routeName: "GetConfigNode", routeValues: new { id = value.Id }))
    .Response<NOFSampleService.CreateConfigNode.NameConflict>(409);
nodes.MapDelete<NOFSampleService.DeleteConfigNode>("/{id:long}")
    .Response<NOFSampleService.DeleteConfigNode.NotFound>(404)
    .Response<NOFSampleService.DeleteConfigNode.HasChildren>(409);
```

Response<T>(status) chooses a fixed status and the type's default representation. Response((T value) => TypedResults.X(...)) replaces that representation with one complete native result; an overload also accepts HttpContext. Native result delegates must return a concrete IResult/IEndpointMetadataProvider type. Plain C# endpoints handle opaque results, async preparation, complex branches, files and SSE.

Each exact result type selects one mapping: endpoint, otherwise nearest group, otherwise built-in. Same-node repeated declarations use the last complete mapping. Different result types remain independent. A late outer declaration never overrides a deeper group. A group mapping for an impossible case is ignored; an impossible explicit endpoint case fails construction. Excluded filters add no cases or schemas. Configure filter failures directly by their concrete case, such as group.Response<RateLimitFilter.Exceeded>(429); HTTP has no Filter-specific category/view.

An endpoint Response<T>(202) replaces a parent's projection/headers entirely and writes T's default body. No individual status/body/header/Problem merge, named variants, selector, dynamic-status provider, ResponseNull or response-context model remains. Headers, Problem fields and Location use native TypedResults/HttpContext in a complete mapping. Native middleware/ProblemDetails services remain host controlled.

Defaults are DTO 200 JSON, Empty 204 without body, allowed null 200 JSON null, declared IFailure 422 ProblemDetails, and exact built-in authentication/permission/policy failures 401/403/400. Explicit 204/304 writes no body. Failure Title/Detail are display content; no names/numbers infer status, duplicate code/message fields or automatic Error flattening. Technical exceptions/cancellation propagate.

## Context and runtime documentation

Context sources remain explicit: authenticated tenant claims, tenant-intent header and named immutable scalar items. No bulk headers, mutable references, HttpContext or service provider flow into semantic Context. Business/filter Context changes do not flow back. Native result callbacks receive HttpContext; they do not receive a final business InvocationContext. Standalone native endpoints can invoke http.InvokeAsync(descriptor, request, explicitContext, ct) through the same identity/policy boundary.

Execution and runtime OpenAPI use the final frozen selection. Known TypedResults contribute static IEndpointMetadataProvider contracts without running response factories. For values such as ProblemHttpResult whose status is instance-specific, supply native .NativeBuilder.ProducesProblem(status); ordinary endpoints likewise use native .Produces*. User functions, switch expressions and IL are never probed. When JSON reflection is disabled, register actual requests, output projections and Problem types in the host JSON context. Native-union metadata should be supplied by generated operations or explicit descriptor case catalogs.

## Breaking migration

Replace Response<T>(r => r.StatusCode(404)) with Response<T>(404). Replace body/header/Problem DSL with one typed native result lambda or an ordinary endpoint. Remove ConfigureProblem, Filter HTTP views, response variants/selectors and HttpOutputContext. Parent mappings are replaced as a whole. Keep Request configuration, generic operation maps, native middleware and the shared invocation policy boundary. Update JSON/OpenAPI metadata and consumers together. No compatibility layer is retained.
