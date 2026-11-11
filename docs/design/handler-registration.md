# Handler Registration

Source generators emit assembly-level `AssemblyInitializeAttribute` initializers.
`builder.AddApplicationPart(assembly)` runs them against the current IServiceCollection.
Initializers add native DI descriptors and update collection-local registries; there is
no process-wide Registry, AddAllHandlers call or service-registration-step pipeline.

```csharp
builder.AddApplicationPart(typeof(NOFSampleService).Assembly);
builder.AddApplicationService<NOFSampleService>();
```

ApplicationServiceAutoInjectGenerator derives each scoped operation registration from
the user-authored Service.Operation base syntax and service contract. It does not read
another generator's output. AddApplicationService registers the service container and
its mappings in ApplicationServiceRegistry; no per-operation host registration is needed.
Operation identity is service contract plus operation name, so operations may share
request/response types. Call the generated server entry:

```csharp
var response = await NOFSampleService.GetRootConfigNodes.InvokeAsync(
    services, request, context, cancellationToken);
```

No client interfaces, HTTP Clients or Local Clients are generated. Server invocation
preserves authorization, tenant normalization and scoped execution policies.

CommandHandlerRegistry, NotificationHandlerRegistry, EventHandlerRegistry,
MappingRegistry and ApplicationServiceRegistry are DI-owned singleton instances and
freeze on first read. Register application parts before execution/transport initialization.
If handlers or mappings are missing, add the assembly containing their generated
initializers, including test-local mappings. NOF hosts already add the calling assembly;
add a separate Application assembly explicitly. Concrete messaging adapters consume
their applicable frozen registries during initialization.

See the [application service migration guide](../en/design/application-requests.md).
