namespace NOF.Hosting.AspNetCore.Extension.OidcServer;

public sealed record OAuthClientCredentialsValidationRequest(
    string ClientId,
    string? ClientSecret,
    IReadOnlySet<string> RequestedScopes,
    string AuthenticationMethod,
    string? ClientAssertion = null,
    string? ClientAssertionType = null,
    string? Audience = null,
    string? GrantType = null);

/// <summary>Contains validated client credentials or an OAuth authentication failure.</summary>
public union OAuthClientCredentialsValidationResult(
    OAuthClientCredentialsValidationResult.Success,
    OAuthClientCredentialsValidationResult.Failure)
{
    /// <summary>Contains the authenticated subject, granted scopes, and access token claims.</summary>
    public sealed record Success(
        string Subject,
        IReadOnlySet<string> Scopes,
        IReadOnlyList<KeyValuePair<string, string>> AccessTokenClaims);

    /// <summary>Contains the OAuth error returned when client authentication fails.</summary>
    public sealed record Failure(string Error, string ErrorDescription);
}
