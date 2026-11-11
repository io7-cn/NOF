# Handler Registration

## Current Model (Sample-Aligned)

NOF no longer depends on calling `AddAllHandlers()` or `Add*AutoInjectServices()` from user code.

The runtime model is:

1. Source generators emit assembly-level initializers (`[assembly: AssemblyInitializeAttribute<...>]`).
2. Initializers write metadata into the builder-owned `Registry`:
- `Registry.AutoInjectRegistry`
- `Registry.RequestHandlerRegistry`
- `Registry.CommandHandlerRegistry`
- `Registry.NotificationHandlerRegistry`
- `Registry.EventHandlerRegistry`
- `MappingRegistry`
3. At startup, `builder.AddApplicationPart(assembly)` executes those initializers.
4. Registry collections freeze on first read; indexed registries build their indexes when frozen.
5. Registration steps wire the runtime:
- `AutoInjectServiceRegistrationStep`
- `RequestHandlerServiceRegistrationStep`
- `HandlerServiceRegistrationStep`

This is the same pattern used by the sample app:

```csharp
builder.AddApplicationPart(typeof(NOFSampleService).Assembly)
    .AddApplicationPart(typeof(TokenAuthorityService).Assembly);
```

## Practical Guidance

- If handlers or mappers are not discovered, first check that the assembly containing them is added via `AddApplicationPart(...)`.
- Keep RPC contracts (`IRpcService`) and their implementations in assemblies that are loaded as application parts.
- Transport integrations such as RabbitMQ consume the frozen handler registries, so stale or missing application parts will prevent consumer registration.
- `AutoInjectRegistry` stores native `ServiceDescriptor` instances; there is no separate `AutoInjectServiceRegistration` model anymore.

## Application use cases

Concrete closed classes implementing IRequestHandler<TRequest,TResponse>, directly or through RequestHandler<TRequest,TResponse>, are discovered by the application source generator. Record classes and inherited closed generic contracts are supported. AddApplicationPart runs the generated initializer once per service collection and registers scoped handlers without runtime type scanning. NOF hosts add the calling assembly automatically. Additional application assemblies need one AddApplicationPart call.

Use case identity is the concrete handler type, not the request/response pair. Multiple handlers may share input/output models; their policy chains remain separate. Generated TryAddScoped registration is idempotent and preserves explicit substitutions. A handler implementing multiple request contracts is still registered once. Explicit AddRequestHandler remains available for tests and selected composition. See the [guide](../en/design/application-requests.md).
