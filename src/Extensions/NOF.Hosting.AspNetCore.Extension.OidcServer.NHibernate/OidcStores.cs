using NHibernate.Linq;
using NOF.Hosting.AspNetCore.Extension.OidcServer;

namespace NOF.Hosting.AspNetCore.Extension.OidcServer.NHibernate;

/// <summary>Uses the host's native scoped session for client persistence.</summary>
public sealed class NHibernateOAuthClientStore(global::NHibernate.ISession context) : IOAuthClientStore
{
    /// <inheritdoc />
    public async Task<OAuthClient?> FindAsync(string clientId, CancellationToken cancellationToken) => await context.Query<OAuthClient>().FirstOrDefaultAsync(value => value.ClientId == clientId, cancellationToken);
    /// <inheritdoc />
    public Task<List<OAuthClient>> ListAsync(CancellationToken cancellationToken) => context.Query<OAuthClient>().OrderBy(value => value.ClientId).ToListAsync(cancellationToken);
    /// <inheritdoc />
    public void Add(OAuthClient value) => context.Save(value);
    /// <inheritdoc />
    public void Remove(OAuthClient value) => context.Delete(value);
}
/// <summary>Uses native queries for the signing-key lifecycle.</summary>
public sealed class NHibernateSigningKeyStore(global::NHibernate.ISession context) : ISigningKeyStore
{
    /// <inheritdoc />
    public Task<List<PersistedSigningKey>> ListPublishedAsync(CancellationToken cancellationToken) => context.Query<PersistedSigningKey>().Where(value => value.Status == PersistedSigningKeyStatus.Active || value.Status == PersistedSigningKeyStatus.NextActive || value.Status == PersistedSigningKeyStatus.Retired).OrderByDescending(value => value.CreatedAtUtc).ToListAsync(cancellationToken);
    /// <inheritdoc />
    public Task<bool> HasStatusAsync(PersistedSigningKeyStatus status, CancellationToken cancellationToken) => context.Query<PersistedSigningKey>().AnyAsync(value => value.Status == status, cancellationToken);
    /// <inheritdoc />
    public void Add(PersistedSigningKey value) => context.Save(value);
    /// <inheritdoc />
    public Task<int> DeleteRevokedAsync(DateTime cutoff, CancellationToken cancellationToken) => context.Query<PersistedSigningKey>().Where(value => value.Status == PersistedSigningKeyStatus.Revoked && value.InvalidatedAtUtc != null && value.InvalidatedAtUtc <= cutoff).DeleteAsync(cancellationToken);
}
/// <summary>Persists and expires revoked refresh-token identities through native queries.</summary>
public sealed class NHibernateRevokedRefreshTokenStore(global::NHibernate.ISession context) : IRevokedRefreshTokenStore
{
    /// <inheritdoc />
    public async Task<RevokedRefreshToken?> FindAsync(string tokenId, CancellationToken cancellationToken) => await context.Query<RevokedRefreshToken>().FirstOrDefaultAsync(value => value.TokenId == tokenId, cancellationToken);
    /// <inheritdoc />
    public void Add(RevokedRefreshToken value) => context.Save(value);
    /// <inheritdoc />
    public Task<int> DeleteExpiredAsync(DateTime cutoff, CancellationToken cancellationToken) => context.Query<RevokedRefreshToken>().Where(value => value.ExpiresAtUtc <= cutoff).DeleteAsync(cancellationToken);
}
