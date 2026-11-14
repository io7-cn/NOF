using NOF.Infrastructure;

namespace NOF.Hosting.AspNetCore.Extension.OidcServer;

public sealed class OAuthClient
{
    public string ClientId { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string SecretHash { get; set; } = string.Empty;

    public string SecretSalt { get; set; } = string.Empty;

    public string JsonWebKeySet { get; set; } = string.Empty;

    public string AllowedScopes { get; set; } = "[]";

    public string RedirectUris { get; set; } = "[]";

    public string AccessTokenClaims { get; set; } = "[]";

    public string TokenEndpointAuthenticationMethod { get; set; } = string.Empty;

    public string AllowedGrantTypes { get; set; } = "[]";

    public string AllowedResponseTypes { get; set; } = "[]";

    public string ApplicationType { get; set; } = OAuthClientApplicationTypes.Web;

    public string RegistrationMetadata { get; set; } = "{}";

    public string RegistrationAccessTokenHash { get; set; } = string.Empty;

    public string RegistrationAccessTokenSalt { get; set; } = string.Empty;

    public OAuthClientType ClientType { get; set; } = OAuthClientType.Confidential;

    public bool IsEnabled { get; set; } = true;

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }
}
