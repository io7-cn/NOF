# Native HTTP adaptation

[简体中文](../../zh-CN/design/http-adaptation.md) · [Complete CRUD slice](configuration-rest.md) · [Expected failures](expected-failures.md)

HTTP belongs to the host. Contract declares business inputs, successful outputs and FailureCase annotations; Application generates operation bases, descriptors and closed results. ASP.NET Core owns routing, authentication/authorization middleware, exception handling, ProblemDetails services, protocols and lifecycle. NOF binds a selected operation to this native boundary.

## Composition and static operation types

```csharp
var builder = WebApplication.CreateBuilder(args).AddNOF();
builder.AddApplicationPart(typeof(NOFSampleService).Assembly);
builder.AddApplicationService<NOFSampleService>();
var app = builder.Build();
app.UseNOF();
app.MapConfigNodeEndpoints();
app.MapOidcServer(); // When the OIDC extension is registered.
app.MapHealthChecks("/health");
app.MapOpenApi();
await app.RunAsync();
```

Feature endpoints are ordinary extension methods returning native RouteGroupBuilder. There is no IHttpEndpointModule registration/discovery, MapNOFEndpoints, ForService or AddNOFHttp. AddNOF registers JSON/OpenAPI/ProblemDetails services; routes, health checks and OIDC mapping remain explicit host calls. One operation may appear on multiple independently configured routes.

```csharp
var nodes = endpoints.MapGroup("/api/config-nodes");
nodes.MapPost<NOFSampleService.CreateConfigNode>("")
    .Response<CreatedConfigNode>(output => output.StatusCode(201)
        .Header("Location", (value, context) => context.LinkTo("GetConfigNode", new { id = value.Id })))
    .Response<NOFSampleService.CreateConfigNode.NameConflict>(output => output.StatusCode(409));
nodes.MapGet<NOFSampleService.GetConfigNodeById>("/{id:long}")
    .WithName("GetConfigNode")
    .Response<GetConfigNodeByIdResponse>(output => output.Body(value => value.Node))
    .Response<NOFSampleService.GetConfigNodeById.NotFound>(output => output.StatusCode(404));
nodes.MapPatch<NOFSampleService.UpdateConfigNodeParent>("/{id:long}/parent")
    .Request(request => request.Route<long>("id").Body<MoveConfigNodeBody>()
        .Map((id, body) => new UpdateConfigNodeParentRequest { NodeId = id, NewParentId = body.ParentId }))
    .Response<NOFSampleService.UpdateConfigNodeParent.NotFound>(output => output.StatusCode(404))
    .Response<NOFSampleService.UpdateConfigNodeParent.ParentCycle>(output => output.StatusCode(409));
```

Generated operation bases implement IServiceOperationDefinition with static abstract Descriptor/Describe. The generic constraint rejects unrelated types at compilation. Static visitor dispatch preserves request type metadata for trimming/AOT without constructing a handler. A small Hosting SourceGenerator emits only the exact Request lambda extension for each referenced operation, using original Contract/Application declarations or referenced metadata. It does not generate clients/routes, inspect function bodies, or depend on another generator running first. The descriptor overload is available for manually defined/dynamic operation catalogs.

## Input binding

Member override wins over a same-name route parameter; remaining GET/HEAD/DELETE members use query, and POST/PUT/PATCH members use JSON body. Header sources are always explicit. Custom input locations use the HttpInputLocation enum. Route/query comparisons default to OrdinalIgnoreCase and can be configured independently through Requests. Naming policies, JsonPropertyName, converters, required/init members, constructor defaults and unknown-member handling come from the host JSON metadata. Repeated query/header values bind arrays or List<T>; scalar duplicates fail.

Convention binding constructs one composite JSON value and deserializes it once. It never deserializes an entire request then mutates route identity through setters. Body metadata contains only body-bound members; a body id cannot override a path id. Missing/invalid input produces 400 and wrong body media produces 415 before handler construction.

Use `.Request(input => input.Member(value => value.NodeId).FromRoute("id"))` for one override. Complete Route/Query/Header/Body source chains end in a typed Map and support up to three sources; exactly one whole Body is allowed. Body() reads the entire request, including collection/scalar JSON. Custom receives HttpContext/cancellation and requires explicit body/input metadata. ByConvention resets complete construction. Member and complete Map cannot be mixed without an explicit reset. Callbacks are never run to discover contracts.

## Response rules

Response<TSource> selects a concrete success/failure branch. StatusCode, Body, each header and each Problem field/extension merge independently. Endpoint direct rules outrank business-group exact-type rules, then Filter defaults, common IFailure/object rules and built-ins. Native deeper group metadata wins within a category; repeated same-scope writes replace only that target. Thus a service header override preserves Filter status/Retry-After. Filter<TFilter> consumes only its static failure directory and never enables execution; excluded cases disappear from runtime and OpenAPI.

Defaults: DTO 200 JSON, Empty 204 without body, permitted null 200 JSON null, declared IFailure 422 ProblemDetails. Exact built-in authentication/permission/policy branches map to 401/403/400. No Title text or number is interpreted as a status. Configure branch types explicitly for 404/409/429.

Body projections accept value or value/context delegates. NoBody explicitly clears the body; RemoveHeader clears one inherited header. Header names compare case-insensitively; Problem extension names retain case. Dynamic StatusCode delegates must declare finite possibleValues; undeclared actual values fail. 204/304 mappings cannot declare a body. Named Response<T>("ready", ...) variants use SelectResponse and mappings.Get<T>(name)/Fallback(value); an unknown or mismatched reference fails.

IFailure.Title/Detail initialize ProblemDetails; type defaults to about:blank, instance is omitted, status equals the HTTP status. Error data is not automatically flattened, and no code/message duplicates are added. ConfigureProblem patches individual fields; Problem(...) or Body<ProblemDetails>(...) replaces the body slot and masks earlier/lower patches. Later/more specific patches still apply. A mismatching factory status fails. IProblemDetailsService performs native host customization/writing. Result(writer, explicit HttpBodyDescription) supports native SSE/file/protocol writers with a declared media/schema contract.

## Context and identity

The boundary authenticates native Http.User or the configured NOF credential provider. Only explicitly selected semantic values flow inward through Context: authenticated tenant claim, tenant-intent header, or named Item sources. No bulk headers, HttpContext, service provider or mutable reference objects are copied into semantic Context. Constants are captured and custom providers must return immutable scalars. Business/filter Context changes do not flow back into output configuration. HttpOutputContext exposes Http, the saved boundary Context, Operation and CancellationToken, with LinkTo for native named routes.

## Runtime OpenAPI and Native AOT

Native endpoint final conventions compile a frozen HttpEndpointBinding from request metadata, operation cases and the effective invocation plan. Execution and the runtime OpenAPI transformer use the same binding. ApiExplorer response metadata, projected body members, parameters, finite statuses, typed headers, named alternatives and Problem extensions follow that description. Startup/document generation never executes handlers, selectors, providers or Map delegates, nor probes lambdas, switches or IL. Hosting JSON contexts must register every actual request/body/projection/extension type when reflection is disabled. The smoke test exercises generic operation maps and OpenAPI with JSON reflection disabled; native publish is also checked in CI.

## Breaking migration

Replace registered endpoint-module classes with explicit MapFeatureEndpoints extensions and call them from the host. Remove MapNOFEndpoints and HttpRequestHeaderOptions; explicitly map health/OIDC and Context sources. Replace manual descriptor route calls with MapGet/MapPost/etc.<Service.Operation> where possible; project-reference consumers must include the Hosting analyzer explicitly, while the NuGet runtime package carries it. Replace failure Code/ErrorCode/Message with Title/Detail and custom FailureCase(name, title, typeof(Error)). Match concrete failures and actual HttpStatusCode; never parse display text. Replace conflicting invocation registration assumptions with per-target last-write rules.
