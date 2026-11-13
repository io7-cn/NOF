using NOF.Abstraction;
using NOF.Application;
using System.Diagnostics;

namespace NOF.Infrastructure;

/// <summary>Normalizes tenant metadata once for every invocation kind.</summary>
public sealed class TenantInvocationFilter : IInvocationFilter
{
    /// <inheritdoc />
    public ValueTask InvokeAsync(InvocationContext context, object input, InvocationDelegate next, CancellationToken cancellationToken)
    {
        var tenantId = context.TryGetItem(NOFAbstractionConstants.Transport.Headers.TenantId, out var value) && value is string tenant
            ? TenantId.Normalize(tenant) : TenantId.Normalize(null);
        Activity.Current?.SetTag(NOFInfrastructureConstants.InboundPipeline.Tags.TenantId, tenantId);
        return next((InvocationContext)context.WithTenantId(tenantId), input, cancellationToken);
    }
}
