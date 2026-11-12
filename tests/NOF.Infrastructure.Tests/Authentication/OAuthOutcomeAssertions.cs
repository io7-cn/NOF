using NOF.Contract;
using NOF.Hosting.AspNetCore.Extension.OidcServer;
using Xunit;

namespace NOF.Contract;

public static class OAuthOutcomeAssertions
{
    public static IssueTokenResponse ExpectSuccess(this TokenIssuance outcome) => Assert.IsType<IssueTokenResponse>(outcome.Value);
    public static ValidateRefreshTokenResponse ExpectSuccess(this RefreshTokenValidation outcome) => Assert.IsType<ValidateRefreshTokenResponse>(outcome.Value);
    public static IntrospectTokenResponse ExpectSuccess(this TokenIntrospection outcome) => Assert.IsType<IntrospectTokenResponse>(outcome.Value);
    public static OAuthClientDescriptor ExpectSuccess(this OAuthClientLookup outcome) => Assert.IsType<OAuthClientDescriptor>(outcome.Value);
    public static OAuthClientSecretDescriptor ExpectSuccess(this OAuthClientSecret outcome) => Assert.IsType<OAuthClientSecretDescriptor>(outcome.Value);
    public static OAuthClientRegistrationSecretDescriptor ExpectSuccess(this OAuthClientRegistration outcome) => Assert.IsType<OAuthClientRegistrationSecretDescriptor>(outcome.Value);
    public static OAuthDeviceAuthorizationResponse ExpectSuccess(this OAuthDeviceAuthorization outcome) => Assert.IsType<OAuthDeviceAuthorizationResponse>(outcome.Value);
    public static OAuthDeviceAuthorizationDescriptor ExpectSuccess(this OAuthDeviceInspection outcome) => Assert.IsType<OAuthDeviceAuthorizationDescriptor>(outcome.Value);
    public static OAuthTokenEndpointResponse ExpectSuccess(this OAuthTokenGrant outcome) => Assert.IsType<OAuthTokenEndpointResponse>(outcome.Value);
    public static CreateOAuthClientRequest ExpectSuccess(this OAuthClientNormalization outcome) => Assert.IsType<CreateOAuthClientRequest>(outcome.Value);
    public static Empty ExpectSuccess(this OAuthMutation outcome) => Assert.IsType<Empty>(outcome.Value);
}
