# Host-selected invocation extensions

[简体中文](../../zh-CN/design/invocation-extensions.md) · [Development](../README.md)

Infrastructure owns `IInvocationFilter`, `InvocationDelegate`, `IInvocationPipeline`, registration rules and effective plans. Application owns `InvocationContext`, operation descriptors and strongly typed business handlers. Semantic `Context` remains in Abstraction. Local event handlers are named `LocalEventHandler<T>`; `IEventPublisher` and current-scope event semantics remain unchanged.

A business handler returns `Task<TBusiness>` (including its generated closed Result union). A filter returns `ValueTask<object?>`, as does `next`; complete invocation returns `Task<object?>`. Filters can return a failure, short circuit with a permitted business value, or inspect/transform the downstream value. No ServiceExecution, ServiceCompleted, ServiceRejected or ServiceRejectedException remains. InvocationContext has no mutable response/rejection state. Context clones preserve invocation identity, not a shared result slot. Technical exceptions and cancellation propagate unchanged.

## Three application scopes

```csharp
services.AddInvocationFilters(filters =>
{
    filters.Global.Use<AuditFilter>();
    filters.Service<IOrderService>().Use<RateLimitFilter>(10);
    filters.UseCase(OrderService.Read.Descriptor).Exclude<RateLimitFilter>();
});
services.UseInMemoryInvocationPipeline();
```

`AddInvocationFilter<T>(order)` is the global shorthand. Registration declares filters; the host chooses whether/how to run them. NOF hosts select the optional in-memory engine. A custom `IInvocationPipeline` must also implement `IInvocationPlanProvider.Describe` and describe its actual execution, rather than every registered filter. Direct DI registration as `IInvocationFilter` does not enable a stage; register a concrete filter/factory and use the configuration API.

The default engine nests Global → Service → UseCase. Service identifies a Contract interface; UseCase identifies a complete descriptor. Each scope orders by numeric order then registration sequence. Repeated same-scope Use/Exclude uses the last declaration, and later explicit order writes replace earlier ones. Deeper scopes override outer rules regardless of registration timing. Duplicate active types run once at their outer position with the effective order; exclude then re-enable at a deeper scope moves the stage into that scope.

Excluded filters contribute no runtime stages or failure descriptions. Rules do not leak to sibling services/operations or other hosts. Rules freeze on plan resolution; late mutation fails. Generated operation mappings and captured implementation types allow startup validation and description without constructing handlers or filters.

Filters resolve lazily in the current scope under the normalized semantic context. The engine binds/restores `Context.Current` around each stage and terminal, validates invocation identity and returned values, and checks cancellation before resolving dependencies. Authorization and tenant selection therefore precede handler construction. Built-in global order remains tracing (-400), instrumentation (-300), optional authentication (-200), tenant (-100), authorization (-50), host tenant (-25).

## Filter-owned failure declarations

```csharp
[FailureCase("Exceeded", "000429")]
[FailureCase("Unavailable", "Limit service unavailable", typeof(LimitDetails))]
public sealed partial class RateLimitFilter : IInvocationFilter
{
    public async ValueTask<object?> InvokeAsync(InvocationContext context, object input,
        InvocationDelegate next, CancellationToken ct)
    {
        if (input is ReadOrderRequest { Page: < 0 })
        {
            return new Exceeded("Invalid page");
        }
        return await next(context, input, ct);
    }
}
```

`FailureCaseAttribute` remains declared in Contract and may also annotate a top-level nongeneric partial filter class. Infrastructure.SourceGenerator generates nested sealed branches and a static, read-only `RateLimitFilter.FailureCases` catalog. It generates no Filter.Result union or factories. All branches implement IFailure with Title/Detail; custom branches also implement IFailure<TError> and expose Error. Name is a C# identifier; Title is a nonblank summary, never a status discriminator. Contract.SourceGenerator reports NOF220 for invalid declarations and still generates no Contract code.

`IInvocationFilter.FailureCases` defaults to empty. Its static virtual `Describe(InvocationDescriptionContext)` defaults to IncludeAll. A custom static description can select its own subset:

```csharp
public static void Describe(InvocationDescriptionContext description)
{
    if (description.Origin == InvocationOrigin.Http)
    {
        description.Include<Exceeded>();
    }
}
```

`Include<TFailure>()` rejects types outside that filter's catalog. Description exposes operation/origin and immutable configuration/attribute metadata, with no scoped service provider or request identity. Built-in authorization description uses the same policy resolver as execution, including real implementation metadata and host tenant/custom-authorizer options.

The effective `InvocationPlan` contains the selected filters, their selected FailureCases and operation BusinessCases. Runtime rejects failures omitted from that effective catalog and unknown result values, including uninitialized native unions. `InvocationResult.Value(value)` unwraps a boxed business union for an adapter's native switch; the execution path preserves the original business value.

Native HTTP feature extensions use generic operation maps, declarative Request/Response rules and runtime OpenAPI from immutable bindings. Exact-type whole-response mappings consume effective failure catalogs and preserve native challenge behavior. See [HTTP adaptation](http-adaptation.md). Message consumption uses the same result path: expected filter failures roll back business work and mark the inbox record failed without a technical retry. Immediate/outbox message sending has no outbound middleware chain.

## Breaking migration

- Rename InMemoryEventHandler<T> to LocalEventHandler<T>; keep IEventPublisher.
- Import NOF.Infrastructure for filter/pipeline interfaces and configuration. Return the result of next or a declared case instead of mutating InvocationContext.
- Match complete invocation results as object values; business handlers retain their closed typed returns. Remove execution wrappers, Reject/SetResponse and exception-based expected failure handling.
- Replace direct IInvocationFilter DI activation with Global/Service/UseCase Use/Exclude declarations.
- Update custom engines to provide their effective plan. Upgrade runtime, generators, adapters and callers together.

[Unified operations](unified-operations.md) · [Expected failures](expected-failures.md) · [Layer boundaries](contract-boundaries.md)
