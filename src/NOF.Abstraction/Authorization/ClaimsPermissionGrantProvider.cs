using System.Security.Claims;

namespace NOF.Abstraction;

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
