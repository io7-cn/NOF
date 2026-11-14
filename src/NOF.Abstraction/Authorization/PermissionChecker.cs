using System.Security.Claims;

namespace NOF.Abstraction;

/// <summary>Supplies grants for an authenticated identity in an explicit semantic context.</summary>
public interface IPermissionGrantProvider
{
    /// <summary>Checks one permission identifier against the current grants. Implementations may query application-owned roles or other grant data.</summary>
    ValueTask<bool> IsGrantedAsync(ClaimsPrincipal user, string permission, Context context, CancellationToken cancellationToken);
}

/// <summary>Checks permissions and complete policies without depending on a transport or an identity protocol.</summary>
public interface IPermissionChecker
{
    /// <summary>Checks one permission for the current scoped user and semantic context.</summary>
    ValueTask<bool> IsGrantedAsync(string permission, CancellationToken cancellationToken = default);
    /// <summary>Checks a policy for an explicit identity and context; permission groups combine with AND.</summary>
    ValueTask<PermissionCheckResult> CheckAsync(AuthorizationPolicy policy, ClaimsPrincipal user, Context context,
        CancellationToken cancellationToken = default);
}

/// <summary>A semantic authorization decision, without HTTP codes or presentation fields.</summary>
public union PermissionCheckResult(PermissionCheckResult.Granted, PermissionCheckResult.AuthenticationRequired, PermissionCheckResult.Denied)
{
    /// <summary>The policy is satisfied.</summary>
    public sealed record Granted;
    /// <summary>The policy requires an authenticated identity.</summary>
    public sealed record AuthenticationRequired;
    /// <summary>An authenticated identity does not satisfy a permission group.</summary>
    public sealed record Denied(IReadOnlyList<string> RequiredPermissions);
}

/// <summary>Reads exact or wildcard permission claims from authenticated identities only.</summary>
public sealed class ClaimsPermissionGrantProvider : IPermissionGrantProvider
{
    /// <inheritdoc />
    public ValueTask<bool> IsGrantedAsync(ClaimsPrincipal user, string permission, Context context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var claims = user.Identities.Where(identity => identity.IsAuthenticated)
            .SelectMany(identity => identity.FindAll(ClaimTypes.Permission));
        return ValueTask.FromResult(claims.Any(claim => !string.IsNullOrWhiteSpace(claim.Value)
            && (string.Equals(claim.Value.Trim(), permission, StringComparison.OrdinalIgnoreCase)
                || claim.Value.Contains('*') && permission.MatchWildcard(claim.Value.Trim(), StringComparison.OrdinalIgnoreCase))));
    }
}

/// <summary>Provides the default policy semantics while allowing the grant source to be replaced independently.</summary>
public sealed class PermissionChecker(IUserContext userContext, Func<IPermissionGrantProvider> grants) : IPermissionChecker
{
    /// <inheritdoc />
    public async ValueTask<bool> IsGrantedAsync(string permission, CancellationToken cancellationToken = default)
        => await CheckAsync(new(true, [new([permission])]), userContext.User, Context.Current, cancellationToken)
            .ConfigureAwait(false) is PermissionCheckResult.Granted;

    /// <inheritdoc />
    public async ValueTask<PermissionCheckResult> CheckAsync(AuthorizationPolicy policy, ClaimsPrincipal user, Context context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(policy);
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        if (policy.RequiresAuthentication && !user.IsAuthenticated)
        {
            return new PermissionCheckResult.AuthenticationRequired();
        }
        using var ambient = Context.PushCurrent(context);
        // Providers may capture tenant-scoped persistence in their constructors. Resolve only after identity/tenant validation.
        var provider = policy.Requirements.Count > 0 ? grants() : null;
        foreach (var group in policy.Requirements)
        {
            var allowed = group.Match == PermissionMatch.All;
            foreach (var permission in group.Permissions)
            {
                var granted = await provider!.IsGrantedAsync(user, permission, context, cancellationToken).ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                if (group.Match == PermissionMatch.All && !granted || group.Match == PermissionMatch.Any && granted)
                {
                    allowed = granted;
                    break;
                }
            }
            if (!allowed)
            {
                return new PermissionCheckResult.Denied(group.Permissions);
            }
        }
        return new PermissionCheckResult.Granted();
    }
}
