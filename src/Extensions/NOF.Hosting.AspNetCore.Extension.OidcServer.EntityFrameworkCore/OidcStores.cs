using Microsoft.EntityFrameworkCore;
using NOF.Hosting.AspNetCore.Extension.OidcServer;

namespace NOF.Hosting.AspNetCore.Extension.OidcServer.EntityFrameworkCore;

/// <summary>Uses the host's native scoped session for client persistence.</summary>
public sealed class EfCoreOAuthClientStore(DbContext context) : IOAuthClientStore
{
    /// <inheritdoc />
    public Task<OAuthClient?> FindAsync(string clientId, CancellationToken cancellationToken) => context.Set<OAuthClient>().FirstOrDefaultAsync(value => value.ClientId == clientId, cancellationToken);
    /// <inheritdoc />
    public Task<List<OAuthClient>> ListAsync(CancellationToken cancellationToken) => context.Set<OAuthClient>().OrderBy(value => value.ClientId).ToListAsync(cancellationToken);
    /// <inheritdoc />
    public void Add(OAuthClient value) => context.Set<OAuthClient>().Add(value);
    /// <inheritdoc />
    public void Remove(OAuthClient value) => context.Set<OAuthClient>().Remove(value);
}
/// <summary>Uses native queries for the signing-key lifecycle.</summary>
public sealed class EfCoreSigningKeyStore(DbContext context) : ISigningKeyStore
{
    /// <inheritdoc />
    public Task<List<PersistedSigningKey>> ListPublishedAsync(CancellationToken cancellationToken) => context.Set<PersistedSigningKey>().Where(value => value.Status == PersistedSigningKeyStatus.Active || value.Status == PersistedSigningKeyStatus.NextActive || value.Status == PersistedSigningKeyStatus.Retired).OrderByDescending(value => value.CreatedAtUtc).ToListAsync(cancellationToken);
    /// <inheritdoc />
    public Task<bool> HasStatusAsync(PersistedSigningKeyStatus status, CancellationToken cancellationToken) => context.Set<PersistedSigningKey>().AnyAsync(value => value.Status == status, cancellationToken);
    /// <inheritdoc />
    public void Add(PersistedSigningKey value) => context.Set<PersistedSigningKey>().Add(value);
    /// <inheritdoc />
    public Task<int> DeleteRevokedAsync(DateTime cutoff, CancellationToken cancellationToken) => context.Set<PersistedSigningKey>().Where(value => value.Status == PersistedSigningKeyStatus.Revoked && value.InvalidatedAtUtc != null && value.InvalidatedAtUtc <= cutoff).ExecuteDeleteAsync(cancellationToken);
}
/// <summary>Persists and expires revoked refresh-token identities through native queries.</summary>
public sealed class EfCoreRevokedRefreshTokenStore(DbContext context) : IRevokedRefreshTokenStore
{
    /// <inheritdoc />
    public Task<RevokedRefreshToken?> FindAsync(string tokenId, CancellationToken cancellationToken) => context.Set<RevokedRefreshToken>().FirstOrDefaultAsync(value => value.TokenId == tokenId, cancellationToken);
    /// <inheritdoc />
    public void Add(RevokedRefreshToken value) => context.Set<RevokedRefreshToken>().Add(value);
    /// <inheritdoc />
    public Task<int> DeleteExpiredAsync(DateTime cutoff, CancellationToken cancellationToken) => context.Set<RevokedRefreshToken>().Where(value => value.ExpiresAtUtc <= cutoff).ExecuteDeleteAsync(cancellationToken);
}
