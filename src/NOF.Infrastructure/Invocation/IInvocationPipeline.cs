using NOF.Application;

namespace NOF.Infrastructure;

/// <summary>Executes the host-selected plan and returns its open result set.</summary>
public interface IInvocationPipeline : IInvocationPlanProvider
{
    /// <summary>Describes a resolved operation with implementation metadata.</summary>
    InvocationPlan Describe(InvocationContext context) => Describe(context.Operation, context.Origin);
    /// <summary>Runs the effective extensions and terminal operation.</summary>
    ValueTask<object?> ExecuteAsync(InvocationContext context, object input, InvocationDelegate terminal, CancellationToken cancellationToken);
}
