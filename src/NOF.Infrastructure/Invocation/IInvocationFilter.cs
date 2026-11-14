using NOF.Application;

namespace NOF.Infrastructure;

/// <summary>Continues execution; business and filter values travel along the same return path.</summary>
public delegate ValueTask<object?> InvocationDelegate(InvocationContext context, object input, CancellationToken cancellationToken);

/// <summary>A host-owned execution extension with static, construction-free failure metadata.</summary>
public interface IInvocationFilter
{
    /// <summary>Gets this filter's generated failure catalog.</summary>
    static virtual IReadOnlyList<InvocationFailureCase> FailureCases => [];
    /// <summary>Selects applicable declared failures; the default includes the entire catalog.</summary>
    static virtual void Describe(InvocationDescriptionContext description) => description.IncludeAll();
    /// <summary>Returns a declared value, or continues to the next stage.</summary>
    ValueTask<object?> InvokeAsync(InvocationContext context, object input, InvocationDelegate next, CancellationToken cancellationToken);
}

/// <summary>Describes the actual execution plan selected by the host.</summary>
public interface IInvocationPlanProvider
{
    /// <summary>Describes an operation without constructing its filters or business handler.</summary>
    InvocationPlan Describe(ServiceOperationDescriptor operation, InvocationOrigin origin);
}

/// <summary>Executes the host-selected plan and returns its open result set.</summary>
public interface IInvocationPipeline : IInvocationPlanProvider
{
    /// <summary>Describes a resolved operation with implementation metadata.</summary>
    InvocationPlan Describe(InvocationContext context) => Describe(context.Operation, context.Origin);
    /// <summary>Runs the effective extensions and terminal operation.</summary>
    ValueTask<object?> ExecuteAsync(InvocationContext context, object input, InvocationDelegate terminal, CancellationToken cancellationToken);
}
