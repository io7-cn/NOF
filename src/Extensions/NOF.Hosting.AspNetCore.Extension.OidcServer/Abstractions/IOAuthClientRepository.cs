using NOF.Contract;

namespace NOF.Hosting.AspNetCore.Extension.OidcServer;

public interface IOAuthClientRepository
{
    ValueTask<OAuthClientCredentialsValidationResult> ValidateClientCredentialsAsync(
        OAuthClientCredentialsValidationRequest request,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<OAuthClientDescriptor>> ListAsync(CancellationToken cancellationToken = default);

    Task<OAuthClientLookup> GetAsync(string clientId, CancellationToken cancellationToken = default);

    Task<OAuthClientSecret> CreateAsync(
        CreateOAuthClientRequest request,
        CancellationToken cancellationToken = default);

    Task<OAuthClientLookup> UpdateAsync(
        string clientId,
        UpdateOAuthClientRequest request,
        CancellationToken cancellationToken = default);

    Task<OAuthClientSecret> RotateSecretAsync(
        string clientId,
        CancellationToken cancellationToken = default);

    Task<OAuthClientMutation> DeleteAsync(string clientId, CancellationToken cancellationToken = default);
}
