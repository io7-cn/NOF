using Microsoft.Extensions.Logging;
using NOF.Application;
using NOF.Contract;
using System.Reflection;
using System.Security.Claims;

namespace NOF.Infrastructure;

public sealed class DefaultInboundAuthorizationHandler(
    ILogger<DefaultInboundAuthorizationHandler> logger) : IInboundAuthorizationHandler
{
    public ValueTask<InboundAuthorizationResult> AuthorizeAsync(
        InboundAuthorizationContext context,
        CancellationToken cancellationToken)
    {
        _ = cancellationToken;

        var policy = InvocationAuthorizationPolicy.Resolve(context.ServiceType, context.ServiceMethodInfo,
            context.HandlerType, context.HandlerMethodInfo);
        foreach (var permission in policy.Requirements)
        {
            var failure = AuthorizeRequirement(context, new PermissionRequirement(permission, false), context.HandlerMethodInfo.Name);
            if (failure is not null)
            {
                return ValueTask.FromResult<InboundAuthorizationResult>(failure);
            }
        }

        logger.LogDebug(
            "Authorization passed for {HandlerType}/{OperationName}",
            context.HandlerType.DisplayName,
            context.HandlerMethodInfo.Name);
        return ValueTask.FromResult<InboundAuthorizationResult>(InboundAuthorizationResult.Success);
    }

    private InboundAuthorizationResult.Denied? AuthorizeRequirement(
        InboundAuthorizationContext context,
        PermissionRequirement? requirement,
        string operationName)
    {
        if (requirement is null)
        {
            return null;
        }

        if (requirement.Value.Permission is null)
        {
            return null;
        }

        if (!context.User.IsAuthenticated)
        {
            logger.LogWarning(
                "Unauthenticated access to {HandlerType}/{OperationName}",
                context.HandlerType.DisplayName,
                operationName);
            return new InboundAuthorizationResult.Denied(
                new Abstraction.Failure("401", "Please login first"),
                Reason: AuthorizationFailureReason.Authentication,
                ChallengePermissions: GetPermissions(requirement.Value));
        }

        var requiredPermissions = GetPermissions(requirement.Value);
        if (requiredPermissions.Count == 0)
        {
            return null;
        }

        var grantedPermissions = ResolveGrantedPermissions(context.User);
        foreach (var permission in requiredPermissions)
        {
            if (HasPermission(grantedPermissions, permission))
            {
                continue;
            }

            logger.LogWarning(
                "Access denied to {HandlerType}/{OperationName} for user without permission {Permission}",
                context.HandlerType.DisplayName,
                operationName,
                permission);
            return new InboundAuthorizationResult.Denied(
                new Abstraction.Failure("403", "Insufficient permissions"),
                Reason: AuthorizationFailureReason.Authorization,
                ChallengePermissions: requiredPermissions);
        }

        return null;
    }

    private static IReadOnlyCollection<string> ResolveGrantedPermissions(ClaimsPrincipal user)
        => user.FindAll(ClaimTypes.Permission)
            .Select(static claim => claim.Value)
            .Where(static permission => !string.IsNullOrWhiteSpace(permission))
            .Select(static permission => permission.Trim())
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static bool HasPermission(IReadOnlyCollection<string> grantedPermissions, string permission)
    {
        if (string.IsNullOrWhiteSpace(permission))
        {
            return false;
        }

        if (grantedPermissions.Contains(permission, StringComparer.OrdinalIgnoreCase))
        {
            return true;
        }

        return grantedPermissions
            .Where(static pattern => pattern.Contains('*'))
            .Any(pattern => permission.MatchWildcard(pattern, StringComparison.OrdinalIgnoreCase));
    }

    private static IReadOnlyList<string> GetPermissions(PermissionRequirement requirement)
        => requirement.RequiresAllPermissions
            ? requirement.Permission?.Split('\u001f', StringSplitOptions.RemoveEmptyEntries) ?? []
            : string.IsNullOrEmpty(requirement.Permission) ? [] : [requirement.Permission];

    private readonly record struct PermissionRequirement(string? Permission, bool RequiresAllPermissions);
}
