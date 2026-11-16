using System.Security.Claims;

namespace NOF.Abstraction;

/// <summary>Supplies grants for an authenticated identity in an explicit semantic context.</summary>
public interface IPermissionGrantProvider
{
    /// <summary>Checks one permission identifier against the current grants. Implementations may query application-owned roles or other grant data.</summary>
    ValueTask<bool> IsGrantedAsync(ClaimsPrincipal user, string permission, Context context, CancellationToken cancellationToken);
}
