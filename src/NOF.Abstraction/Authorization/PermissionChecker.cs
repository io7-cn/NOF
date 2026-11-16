using System.Security.Claims;

namespace NOF.Abstraction;

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
