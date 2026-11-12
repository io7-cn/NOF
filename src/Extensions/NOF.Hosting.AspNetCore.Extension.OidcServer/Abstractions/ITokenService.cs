using NOF.Contract;

namespace NOF.Hosting.AspNetCore.Extension.OidcServer;

public interface ITokenService
{
    Task<TokenIssuance> IssueTokenAsync(IssueTokenRequest request, CancellationToken cancellationToken);

    Task<RefreshTokenValidation> ValidateRefreshTokenAsync(ValidateRefreshTokenRequest request, CancellationToken cancellationToken);

    Task<OAuthMutation> RevokeRefreshTokenAsync(RevokeRefreshTokenRequest request, CancellationToken cancellationToken);

    Task<TokenIntrospection> IntrospectTokenAsync(IntrospectTokenRequest request, CancellationToken cancellationToken);
}
