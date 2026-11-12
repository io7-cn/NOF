using NOF.Contract;

namespace NOF.Hosting.AspNetCore.Extension.OidcServer;

public interface IOAuthDeviceGrantService
{
    Task<OAuthDeviceAuthorization> CreateAsync(
        CreateOAuthDeviceGrantRequest request,
        CancellationToken cancellationToken = default);

    Task<OAuthDeviceInspection> GetPendingAsync(
        string userCode,
        CancellationToken cancellationToken = default);

    Task<OAuthMutation> ApproveAsync(
        string userCode,
        string subject,
        CancellationToken cancellationToken = default);

    Task<OAuthMutation> DenyAsync(
        string userCode,
        CancellationToken cancellationToken = default);

    Task<OAuthTokenGrant> RedeemAsync(
        string deviceCode,
        string clientId,
        CancellationToken cancellationToken = default);
}
