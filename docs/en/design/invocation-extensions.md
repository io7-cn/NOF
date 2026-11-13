# Host-selected invocation extensions

[简体中文](../../zh-CN/design/invocation-extensions.md) · [Development](../README.md)

NOF.Application defines one extension point for service operations, commands and notifications:
`IInvocationFilter.InvokeAsync(InvocationContext, object, InvocationDelegate, CancellationToken)`.
Application owns InvocationContext, which composes semantic Context and supplies an operation descriptor, invocation origin, input/handler metadata and an execution outcome. It has no HTTP dependency.
Expected business outcomes remain operation-specific unions; policy rejection is separate.

Registering an extension does not select an execution engine or cause it to run. The host owns
`IInvocationPipeline`, including whether, where and in what order extensions execute. The optional
NOF.Infrastructure in-memory engine is the default selected by NOF host builders.

```csharp
// Application composition: a reusable extension definition, without selecting an engine.
services.AddInvocationFilter<AuditFilter>(order: 0);

// Host composition: choose the default, or supply your own IInvocationPipeline.
services.UseInMemoryInvocationPipeline();
// services.AddScoped<IInvocationPipeline, MyHostInvocationPipeline>();
```

`AddInvocationFilter<T>` registers a scoped filter and one immutable definition per filter type.
The default engine executes ascending numeric order, preserving registration order for ties.
It resolves each filter only when its stage runs, under the current execution context. This lets
normalization and authorization run before later filter dependencies and business handlers resolve.
Direct DI registrations of `IInvocationFilter` also work at order zero, with ordinary DI construction
semantics; use the definition API when constructor timing matters. Filters call `next` to continue,
can replace immutable context items through a context clone, and may short circuit. Clones share
the outcome. The default engine binds and restores `Context.Current` around every stage and terminal,
checks cancellation and propagates exceptions unchanged.

```csharp
public sealed class AuditFilter(ILogger<AuditFilter> logger) : IInvocationFilter
{
    public async ValueTask InvokeAsync(InvocationContext context, object input,
        InvocationDelegate next, CancellationToken cancellationToken)
    {
        logger.LogInformation("Invoking {Operation}", context.OperationName);
        await next(context, input, cancellationToken);
    }
}
```

The built-in defaults are tracing (-400), instrumentation (-300), optional resource-server
authentication (-200), tenant normalization (-100), authorization (-50) and host-tenant switching
(-25). These are defaults of this host configuration, not ordering rules imposed on every host.
A replacement engine must deliberately preserve its required authentication, authorization, tenant,
cancellation and scope guarantees. Calling a handler directly bypasses the boundary. Service and
message adapters provide the terminal; they no longer assemble independent middleware chains.

HTTP routing, binding, native middleware, endpoint filters, ProblemDetails and exception presentation
stay in ASP.NET Core. HTTP endpoints invoke the application boundary through `HttpContext.InvokeAsync`;
they do not adapt HTTP middleware into a second application middleware stack. Technical exceptions
reach the host's exception handling; tracing records and rethrows them. Cancellation is not a business rejection.

Message sending has no extensible outbound middleware chain. `MessageDelivery` prepares message ID,
normalized tenant and producer Activity/trace headers for both immediate delivery and outbox staging.
Arbitrary context items and user principals are not serialized. Outbox staging still requires saving
the same database context, and ordered delivery, retries and idempotency remain delivery responsibilities.
Transport-specific send policies belong in the selected rider or host integration.

## Breaking migration

Remove all five request/command/notification inbound and command/notification outbound middleware
interfaces, their delegates, outbound context types and `Add*Middleware` registrations. Implement
`IInvocationFilter` once, register with `AddInvocationFilter`, and select a host engine. Replace
`Compare` topology constraints with host-owned ordering in the default engine. The outbound dispatcher
injection analyzer is removed with its extension surface. All entry points use one sealed InvocationContext; the three inbound context subclasses and InvocationKind are removed. Context.Current carries invocation.Context.

Standalone test/service providers must choose `UseInMemoryInvocationPipeline()` explicitly;
NOF host builders already do so. Custom hosts can replace `IInvocationPipeline` without changing
application filters. Upgrade application, infrastructure and hosting packages together.

[Current unified operation and message binding API](unified-operations.md).

[Current Contract/Application/Abstraction boundaries](contract-boundaries.md).
