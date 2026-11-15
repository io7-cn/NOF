using NOF.Abstraction;
using NOF.Application;
using NOF.Contract;
using System.Diagnostics;

namespace NOF.Infrastructure;

/// <summary>Normalizes tenant metadata once for every invocation kind.</summary>
public sealed class TenantInvocationFilter : IInvocationFilter
{
    /// <inheritdoc />
    public ValueTask<object?> InvokeAsync(InvocationContext context, object input, InvocationDelegate next, CancellationToken cancellationToken)
    {
        var tenantId = context.Context.TenantId;
        Activity.Current?.SetTag(NOFInfrastructureConstants.InboundPipeline.Tags.TenantId, tenantId);
        return next((InvocationContext)context.WithTenantId(tenantId), input, cancellationToken);
    }
}
