namespace NOF.Hosting.AspNetCore.Extension.OidcServer;

/// <summary>Stores OIDC-owned state with feature-specific operations in the current unit of work.</summary>
public interface IRevokedRefreshTokenStore
{
    /// <summary>Loads a tracked revocation record by token identity.</summary>
    Task<RevokedRefreshToken?> FindAsync(string tokenId, CancellationToken cancellationToken);
    /// <summary>Stages a token revocation record.</summary>
    void Add(RevokedRefreshToken token);
    /// <summary>Deletes expired token revocation records.</summary>
    Task<int> DeleteExpiredAsync(DateTime cutoff, CancellationToken cancellationToken);
}
