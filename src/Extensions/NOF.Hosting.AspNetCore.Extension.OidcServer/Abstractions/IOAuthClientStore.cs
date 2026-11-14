namespace NOF.Hosting.AspNetCore.Extension.OidcServer;

/// <summary>Stores OIDC-owned state with feature-specific operations in the current unit of work.</summary>
public interface IOAuthClientStore
{
    /// <summary>Loads the client tracked by the current provider scope.</summary>
    Task<OAuthClient?> FindAsync(string clientId, CancellationToken cancellationToken);
    /// <summary>Loads registered clients for management queries.</summary>
    Task<List<OAuthClient>> ListAsync(CancellationToken cancellationToken);
    /// <summary>Stages a new client.</summary>
    void Add(OAuthClient client);
    /// <summary>Stages removal of a client.</summary>
    void Remove(OAuthClient client);
}
