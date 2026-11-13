using NOF.Abstraction;
using NOF.Application;
using NOF.Contract;
using NOF.Hosting;
using System.Diagnostics;

namespace NOF.Infrastructure;

/// <summary>
/// Applies contract-level host-tenant selection after authorization and before application service handler resolution.
/// </summary>
public sealed class HostTenantInvocationFilter : IInvocationFilter
{
    /// <inheritdoc />
    public async ValueTask<object?> InvokeAsync(InvocationContext context, object request,
        InvocationDelegate next, CancellationToken cancellationToken)
    {
        if (!ServiceOperationInspector.RequiresHostTenant(context.ServiceMethodInfo))
        {
            return await next(context, request, cancellationToken);
        }

        var hostContext = context.WithTenantId(NOFAbstractionConstants.Tenant.HostId);
        Activity.Current?.SetTag(NOFInfrastructureConstants.InboundPipeline.Tags.TenantId, hostContext.TenantId);
        using var _ = Context.PushCurrent(hostContext.Context);
        return await next(hostContext, request, cancellationToken);
    }
}
