using Microsoft.Extensions.Logging;
using NOF.Abstraction;
using NOF.Application;

namespace NOF.Infrastructure;

/// <summary>Adapts the shared permission checker to invocation failures without reinterpreting declarations.</summary>
public sealed class DefaultInboundAuthorizationHandler(IPermissionChecker permissions,
    ILogger<DefaultInboundAuthorizationHandler> logger) : IInboundAuthorizationHandler
{
    /// <inheritdoc />
    public async ValueTask<InboundAuthorizationResult> AuthorizeAsync(InboundAuthorizationContext context, CancellationToken cancellationToken)
    {
        var decision = await permissions.CheckAsync(context.Policy, context.User, context.ExecutionContext, cancellationToken).ConfigureAwait(false);
        switch (decision)
        {
            case PermissionCheckResult.Granted:
                logger.LogDebug("Authorization passed for {HandlerType}/{OperationName}", context.HandlerType, context.HandlerMethodInfo.Name);
                return InboundAuthorizationResult.Success;
            case PermissionCheckResult.AuthenticationRequired:
                return new InboundAuthorizationResult.Denied(new("401", "Please login first"), AuthorizationFailureReason.Authentication, []);
            case PermissionCheckResult.Denied denied:
                return new InboundAuthorizationResult.Denied(new("403", "Insufficient permissions"), AuthorizationFailureReason.Authorization, denied.RequiredPermissions);
            case null:
                throw new InvalidOperationException("The permission checker returned an uninitialized decision.");
        }
    }
}
