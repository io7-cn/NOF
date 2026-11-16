using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NOF.Abstraction;
using NOF.Application;

namespace NOF.Infrastructure;

/// <summary>Optional default engine; execution and descriptions share exactly one effective plan.</summary>
public sealed class InMemoryInvocationPipeline(IServiceProvider services, InvocationPlanFactory plans) : IInvocationPipeline
{
    /// <inheritdoc />
    public InvocationPlan Describe(OperationDescriptor operation, InvocationOrigin origin) => plans.Describe(operation, origin);
    /// <inheritdoc />
    public InvocationPlan Describe(InvocationContext context) => plans.Describe(context);
    /// <inheritdoc />
    public async ValueTask<object?> ExecuteAsync(InvocationContext context, object input, InvocationDelegate terminal, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var plan = Describe(context);
        InvocationDelegate pipeline = async (current, value, token) =>
        {
            ValidateIdentity(context, current);
            token.ThrowIfCancellationRequested();
            using var ambient = Context.PushCurrent(current.Context);
            return await terminal(current, value, token).ConfigureAwait(false);
        };
        for (var index = plan.Filters.Count - 1; index >= 0; index--)
        {
            var filter = plan.Filters[index];
            var next = pipeline;
            pipeline = async (current, value, token) =>
            {
                ValidateIdentity(context, current);
                token.ThrowIfCancellationRequested();
                using var ambient = Context.PushCurrent(current.Context);
                return plan.Validate(await filter.Resolve(services).InvokeAsync(current, value, next, token).ConfigureAwait(false));
            };
        }
        return plan.Validate(await pipeline(context, input, cancellationToken).ConfigureAwait(false));
    }
    private static void ValidateIdentity(InvocationContext original, InvocationContext current)
    {
        if (original.Operation != current.Operation || original.HandlerType != current.HandlerType
            || original.HandlerMethodInfo != current.HandlerMethodInfo || original.Origin != current.Origin
            || original.InvocationId != current.InvocationId)
        {
            throw new InvalidOperationException("A filter may normalize semantic context, not replace invocation identity or operation metadata.");
        }
    }
}
