namespace NOF.Hosting.AspNetCore.Extension.OidcServer;

/// <summary>Stores OIDC-owned state with feature-specific operations in the current unit of work.</summary>
public interface ISigningKeyStore
{
    /// <summary>Loads active, prepared and retained signing keys.</summary>
    Task<List<PersistedSigningKey>> ListPublishedAsync(CancellationToken cancellationToken);
    /// <summary>Checks whether a key exists in the selected lifecycle state.</summary>
    Task<bool> HasStatusAsync(PersistedSigningKeyStatus status, CancellationToken cancellationToken);
    /// <summary>Stages a new signing key.</summary>
    void Add(PersistedSigningKey key);
    /// <summary>Deletes revoked keys whose invalidation retention has expired.</summary>
    Task<int> DeleteRevokedAsync(DateTime cutoff, CancellationToken cancellationToken);
}
