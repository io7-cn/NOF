# NOF.Abstraction

Cross-cutting abstractions package for the [NOF Framework](https://github.com/XmmShp/NOF).

## Overview

Provides shared contracts and annotations intended for use across layers:

- `[AutoInject]` via `Microsoft.Extensions.DependencyInjection`
- the foundational `NOF.Abstraction.Context` execution context type
- async-flow ambient execution context access through `Context.Current`
- `LocalEventHandler<TEvent>` / `IEventPublisher`
- `AddNOFAbstraction()` for package-local runtime registration
- ambient event publishing helpers via `EventPublisher` and `PublishAsEvent(...)`

## In-Memory Events

NOF provides a scoped in-memory event mechanism for invoking distributed handlers within the same dependency injection scope.

- Any non-null `object` can be used as an in-memory event payload
- `LocalEventHandler<TEvent>` handles that payload together with its `Context`
- `IEventPublisher` dispatches the event and the same `Context` instance to all handlers resolved from the current scope

This mechanism can be used by domain aggregates, application services, or any other in-scope collaboration that should remain in-process.

For convenience, NOF also exposes an ambient publisher facade:

- `payload.PublishAsEvent()` uses the ambient `IEventPublisher` and its bound `Context` for the current async flow
- `payload.PublishAsEvent(context)` explicitly overrides the context used by the ambient publisher
- `payload.PublishAsEvent(publisher)` is the explicit alternative when ambient scope is not desired
- `payload.PublishAsEvent(publisher, context)` keeps both the publisher and context explicit
- `AddNOFAbstraction()` registers the scoped publisher and explicit event registry

Managed HTTP/operation/message/test entries bind and restore ambient services and Context. Ordinary DI scopes have no activation protocol. Standalone Application code uses using var binding = services.BindNOF(context); package-only callers may use EventPublisher.PushCurrent(publisher). PublishAsEvent keeps its synchronous timing and exception behavior. See [ambient binding](../../docs/en/design/ambient-binding.md) ([简体中文](../../docs/zh-CN/design/ambient-binding.md)).

```csharp
public sealed class OrderCreatedHandler : LocalEventHandler<OrderCreated>
{
    public override Task HandleAsync(
        OrderCreated @event,
        Context context,
        CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}

orderCreated.PublishAsEvent();
await eventPublisher.PublishAsync(orderCreated, context, cancellationToken);
```

## Installation

```shell
dotnet add package NOF.Abstraction
```

## Runtime Registration

```csharp
using Microsoft.Extensions.DependencyInjection;

services.AddNOFAbstraction();
```

`AddNOFAbstraction()` registers explicit services; it does not activate an ambient scope merely because DI resolves a service.

## Auto Injection

`[AutoInject]` lives in the official dependency injection namespace so it can be used with `ServiceLifetime` from a single using:

```csharp
using Microsoft.Extensions.DependencyInjection;

[AutoInject(ServiceLifetime.Scoped)]
public sealed class MyService : IMyService;
```

## JSON And AOT

`NOF.Abstraction` exposes the shared `JsonSerializerOptions.NOF` instance used across the framework.

- It includes `NOFJsonSerializerContext` for common primitive and framework-adjacent types.
- It stays compatible with normal JIT execution.
- In AOT-oriented apps, register your own source-generated contexts before a type is first serialized or deserialized.

```csharp
using System.Text.Json;

JsonSerializerOptions.ConfigureNOFJsonSerializerOptions(options =>
{
    options.TypeInfoResolverChain.Add(MyAppJsonSerializerContext.Default);
});
```

If a type is missing JSON metadata, NOF throws an `InvalidOperationException` that includes the concrete type name and points you at `ConfigureNOFJsonSerializerOptions(...)`.

## License

Apache-2.0

`Failure(code, message)` is a shared value for policy rejection and protocol adapters.
Predictable business failures use Contract declarations and Application-generated cases;
Domain exceptions and the former Throw extension are removed.

Semantic Context is exposed directly as NOF.Abstraction.Context. The former Contract namespace/type-forwarder is removed; execution models live in Application. See [boundaries](../../docs/en/design/contract-boundaries.md).

JSON defaults are fresh instances from JsonSerializerOptions.CreateNOF(); runtime hosts configure their own serializer options. There is no process-wide mutable configuration. See [host composition](../../docs/en/design/host-composition.md).
