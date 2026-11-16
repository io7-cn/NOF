using System.Security.Claims;

namespace NOF.Abstraction;

/// <summary>Checks permissions and complete policies without depending on a transport or an identity protocol.</summary>
public interface IPermissionChecker
{
    /// <summary>Checks one permission for the current scoped user and semantic context.</summary>
    ValueTask<bool> IsGrantedAsync(string permission, CancellationToken cancellationToken = default);
    /// <summary>Checks a policy for an explicit identity and context; permission groups combine with AND.</summary>
    ValueTask<PermissionCheckResult> CheckAsync(AuthorizationPolicy policy, ClaimsPrincipal user, Context context,
        CancellationToken cancellationToken = default);
}
