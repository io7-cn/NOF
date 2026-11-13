using NOF.Abstraction;
using NOF.Contract;
using NOF.Hosting.AspNetCore.Extension.OidcServer;
using Xunit;

namespace NOF.Infrastructure.Tests.Authentication;

public class UnionOutcomeTests
{
    [Fact]
    public void AuthorizationOutcome_ShouldPreserveDenialAndMatchEveryCase()
    {
        var failure = NOF.Testing.TestOutcome.Fail("403", "Permission required");
        InboundAuthorizationResult allowed = InboundAuthorizationResult.Success;
        InboundAuthorizationResult denied = new InboundAuthorizationResult.Denied(failure, RejectionReason.Authorization, ["orders.read"]);

        Assert.True(IsAllowed(allowed));
        Assert.False(IsAllowed(denied));
        Assert.False(IsAllowed(default));
        var denial = Assert.IsType<InboundAuthorizationResult.Denied>(denied.Value);
        Assert.Same(failure, denial.Result);
        Assert.Equal(["orders.read"], denial.ChallengePermissions);

        static bool IsAllowed(InboundAuthorizationResult result) => result switch
        {
            InboundAuthorizationResult.Allowed => true,
            InboundAuthorizationResult.Denied => false,
            null => false
        };
    }

    [Fact]
    public void ClientCredentialsOutcome_ShouldPreserveSuccessAndFailure()
    {
        var success = new OAuthClientCredentialsValidationResult.Success("client", new HashSet<string> { "orders.read" }, []);
        OAuthClientCredentialsValidationResult validated = success;
        OAuthClientCredentialsValidationResult rejected = new OAuthClientCredentialsValidationResult.Failure("invalid_client", "Invalid credentials");

        Assert.Same(success, validated.Value);
        Assert.Equal("client", Describe(validated));
        Assert.Equal("invalid_client", Describe(rejected));
        Assert.Equal("empty", Describe(default));

        static string Describe(OAuthClientCredentialsValidationResult result) => result switch
        {
            OAuthClientCredentialsValidationResult.Success value => value.Subject,
            OAuthClientCredentialsValidationResult.Failure value => value.Error,
            null => "empty"
        };
    }

    [Fact]
    public void TokenExchangeOutcome_ShouldPreserveClaimsAndMatchEveryCase()
    {
        TokenClaim[] claims = [new("permission", "orders.read")];
        OAuthTokenExchangeResult exchanged = new OAuthTokenExchangeResult.Success("user", new HashSet<string> { "orders.read" }, claims);
        OAuthTokenExchangeResult rejected = new OAuthTokenExchangeResult.Failure("invalid_grant", "Invalid subject");

        Assert.Same(claims, Assert.IsType<OAuthTokenExchangeResult.Success>(exchanged.Value).AccessTokenClaims);
        Assert.Equal("user", Describe(exchanged));
        Assert.Equal("invalid_grant", Describe(rejected));
        Assert.Equal("empty", Describe(default));

        static string Describe(OAuthTokenExchangeResult result) => result switch
        {
            OAuthTokenExchangeResult.Success value => value.Subject,
            OAuthTokenExchangeResult.Failure value => value.Error,
            null => "empty"
        };
    }
}
