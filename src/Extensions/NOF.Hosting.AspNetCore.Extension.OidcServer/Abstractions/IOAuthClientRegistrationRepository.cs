using NOF.Contract;

namespace NOF.Hosting.AspNetCore.Extension.OidcServer;

public interface IOAuthClientRegistrationRepository
{
    Task<OAuthClientRegistration> RegisterAsync(
        CreateOAuthClientRequest request,
        CancellationToken cancellationToken = default);

    Task<OAuthClientRegistration> GetAsync(
        string clientId,
        string registrationAccessToken,
        CancellationToken cancellationToken = default);

    Task<OAuthClientRegistration> UpdateAsync(
        string clientId,
        string registrationAccessToken,
        string? currentClientSecret,
        UpdateOAuthClientRequest request,
        CancellationToken cancellationToken = default);

    Task<OAuthClientMutation> DeleteAsync(
        string clientId,
        string registrationAccessToken,
        CancellationToken cancellationToken = default);
}
