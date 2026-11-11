using Microsoft.EntityFrameworkCore;
using NOF.Contract;
using NOF.Hosting;
using NOF.Hosting.AspNetCore.Extension.OidcServer;
using NOF.Infrastructure.EntityFrameworkCore;
using NOF.Test;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Xunit;

namespace NOF.Infrastructure.Tests.Authentication.Services;

public sealed class TokenAuthorityServiceTests
{
    private const string SigningKeyEncryptionKey = "jwt-signing-key-passphrase-for-tests";

    [Fact]
    public async Task IssueToken_ShouldIssueClaimsDrivenAccessAndRefreshTokens()
    {
        var builder = CreateOidcServerBuilder();

        await using var host = await builder.BuildTestHostAsync();
        using var scope = host.CreateScope();
        var tokenService = scope.GetRequiredService<ITokenService>();

        var generateResult = await tokenService.IssueTokenAsync(
            new IssueTokenRequest
            {
                Audience = "nof-tests",
                ClientId = "web-client",
                AccessTokenExpiration = TimeSpan.FromMinutes(10),
                AccessClaims =
                [
                    new(JwtRegisteredClaimNames.Sub, "user-1"),
                    new(ClaimTypes.TenantId, "tenant-1"),
                    new(ClaimTypes.Permission, "printer.read"),
                    new(ClaimTypes.Permission, "printer.write")
                ],
                RefreshToken = new RefreshTokenOptions
                {
                    Expiration = TimeSpan.FromHours(12),
                    Claims =
                    [
                        new(JwtRegisteredClaimNames.Sub, "user-1")
                    ]
                }
            },
            CancellationToken.None);

        Assert.NotNull(generateResult.ExpectSuccess());
        Assert.False(string.IsNullOrWhiteSpace(generateResult.ExpectSuccess().AccessToken));
        Assert.NotNull(generateResult.ExpectSuccess().RefreshToken);
        Assert.NotEqual(default, generateResult.ExpectSuccess().RefreshToken!.ExpiresAtUtc);

        var accessToken = new JwtSecurityTokenHandler().ReadJwtToken(generateResult.ExpectSuccess().AccessToken);
        Assert.False(string.IsNullOrWhiteSpace(accessToken.Header.Kid));
        Assert.Equal("at+jwt", accessToken.Header.Typ);
        Assert.Contains(accessToken.Claims, claim => claim.Type == JwtRegisteredClaimNames.Sub && claim.Value == "user-1");
        Assert.Contains(accessToken.Claims, claim => claim.Type == OAuthClaimTypes.ClientId && claim.Value == "web-client");
        Assert.Contains(accessToken.Claims, claim => claim.Type == JwtRegisteredClaimNames.Iat);
        Assert.Contains(accessToken.Claims, claim => claim.Type == JwtRegisteredClaimNames.Jti);
        Assert.Contains(accessToken.Claims, claim => claim.Type == ClaimTypes.TenantId && claim.Value == "tenant-1");
        Assert.Equal(2, accessToken.Claims.Count(claim => claim.Type == ClaimTypes.Permission));

        var refreshToken = generateResult.ExpectSuccess().RefreshToken;
        Assert.NotNull(refreshToken);
        var refreshJwt = new JwtSecurityTokenHandler().ReadJwtToken(refreshToken.Token);
        Assert.Equal("JWT", refreshJwt.Header.Typ);
        Assert.Contains(refreshJwt.Claims, claim => claim.Type == OAuthClaimTypes.ClientId && claim.Value == "web-client");

        var validateResult = await tokenService.ValidateRefreshTokenAsync(
            new ValidateRefreshTokenRequest
            {
                RefreshToken = refreshToken.Token
            },
            CancellationToken.None);

        Assert.NotNull(validateResult.ExpectSuccess());
        Assert.False(string.IsNullOrWhiteSpace(validateResult.ExpectSuccess().TokenId));
        Assert.Contains(validateResult.ExpectSuccess().Claims, claim => claim.Type == JwtRegisteredClaimNames.Jti);
        Assert.Contains(validateResult.ExpectSuccess().Claims, claim => claim.Type == JwtRegisteredClaimNames.Sub && claim.Value == "user-1");
        Assert.DoesNotContain(validateResult.ExpectSuccess().Claims, claim => claim.Type == ClaimTypes.TenantId);
    }

    [Fact]
    public async Task IssueToken_WithoutRefreshToken_ShouldOnlyIssueAccessToken()
    {
        var builder = CreateOidcServerBuilder();

        await using var host = await builder.BuildTestHostAsync();
        using var scope = host.CreateScope();
        var tokenService = scope.GetRequiredService<ITokenService>();

        var generateResult = await tokenService.IssueTokenAsync(
            new IssueTokenRequest
            {
                Audience = "nof-tests",
                ClientId = "service-1",
                AccessTokenExpiration = TimeSpan.FromMinutes(5),
                AccessClaims =
                [
                    new(JwtRegisteredClaimNames.Sub, "service-1")
                ]
            },
            CancellationToken.None);

        Assert.NotNull(generateResult.ExpectSuccess());
        Assert.False(string.IsNullOrWhiteSpace(generateResult.ExpectSuccess().AccessToken));
        Assert.Null(generateResult.ExpectSuccess().RefreshToken);
    }

    [Fact]
    public async Task IssueToken_ShouldSerializeNumericDateClaimsAsNumbers()
    {
        var builder = CreateOidcServerBuilder();
        const string issuedAt = "1710000000";

        await using var host = await builder.BuildTestHostAsync();
        using var scope = host.CreateScope();
        var tokenService = scope.GetRequiredService<ITokenService>();

        var generateResult = await tokenService.IssueTokenAsync(
            new IssueTokenRequest
            {
                Audience = "nof-tests",
                ClientId = "web-client",
                AccessTokenExpiration = TimeSpan.FromMinutes(5),
                AccessClaims =
                [
                    new(JwtRegisteredClaimNames.Sub, "user-1"),
                    TokenClaim.Integer64(JwtRegisteredClaimNames.Iat, long.Parse(issuedAt)),
                    new("auth_time", issuedAt)
                ]
            },
            CancellationToken.None);

        Assert.NotNull(generateResult.ExpectSuccess());
        var payloadJson = Encoding.UTF8.GetString(Base64UrlDecode(generateResult.ExpectSuccess().AccessToken.Split('.')[1]));
        using var payload = JsonDocument.Parse(payloadJson);
        Assert.Equal(JsonValueKind.Number, payload.RootElement.GetProperty(JwtRegisteredClaimNames.Iat).ValueKind);
        Assert.Equal(JsonValueKind.Number, payload.RootElement.GetProperty("auth_time").ValueKind);
    }

    [Fact]
    public async Task IssueToken_ShouldSerializeExplicitArrayClaimsAsJsonArrays()
    {
        var builder = CreateOidcServerBuilder();

        await using var host = await builder.BuildTestHostAsync();
        using var scope = host.CreateScope();
        var tokenService = scope.GetRequiredService<ITokenService>();

        var generateResult = await tokenService.IssueTokenAsync(
            new IssueTokenRequest
            {
                Audience = "nof-tests",
                ClientId = "web-client",
                AccessTokenExpiration = TimeSpan.FromMinutes(5),
                AccessClaims =
                [
                    new(JwtRegisteredClaimNames.Sub, "user-1"),
                    TokenClaim.Array(OAuthClaimTypes.Groups, "engineering", "ops")
                ]
            },
            CancellationToken.None);

        Assert.NotNull(generateResult.ExpectSuccess());

        var payloadJson = Encoding.UTF8.GetString(Base64UrlDecode(generateResult.ExpectSuccess().AccessToken.Split('.')[1]));
        using var payload = JsonDocument.Parse(payloadJson);
        var groups = payload.RootElement.GetProperty(OAuthClaimTypes.Groups);

        Assert.Equal(JsonValueKind.Array, groups.ValueKind);
        Assert.Equal(["engineering", "ops"], groups.EnumerateArray().Select(static item => item.GetString()!).ToArray());
    }

    [Fact]
    public async Task IssueToken_ShouldSerializeJsonClaimsAsJsonObjects()
    {
        var builder = CreateOidcServerBuilder();

        await using var host = await builder.BuildTestHostAsync();
        using var scope = host.CreateScope();
        var tokenService = scope.GetRequiredService<ITokenService>();

        var generateResult = await tokenService.IssueTokenAsync(
            new IssueTokenRequest
            {
                Audience = "nof-tests",
                ClientId = "service-a",
                AccessTokenExpiration = TimeSpan.FromMinutes(5),
                AccessClaims =
                [
                    new(JwtRegisteredClaimNames.Sub, "user-1"),
                    TokenClaim.Json(OAuthClaimTypes.Actor, """{"sub":"client:order-service","act":{"sub":"client:web-app"}}""")
                ]
            },
            CancellationToken.None);

        Assert.NotNull(generateResult.ExpectSuccess());

        var payloadJson = Encoding.UTF8.GetString(Base64UrlDecode(generateResult.ExpectSuccess().AccessToken.Split('.')[1]));
        using var payload = JsonDocument.Parse(payloadJson);
        var actor = payload.RootElement.GetProperty(OAuthClaimTypes.Actor);

        Assert.Equal(JsonValueKind.Object, actor.ValueKind);
        Assert.Equal("client:order-service", actor.GetProperty(OAuthClaimTypes.Subject).GetString());
        Assert.Equal("client:web-app", actor.GetProperty(OAuthClaimTypes.Actor).GetProperty(OAuthClaimTypes.Subject).GetString());
    }

    [Fact]
    public async Task IssueToken_WithoutClientId_ShouldFail()
    {
        var builder = CreateOidcServerBuilder();

        await using var host = await builder.BuildTestHostAsync();
        using var scope = host.CreateScope();
        var tokenService = scope.GetRequiredService<ITokenService>();

        var generateResult = await tokenService.IssueTokenAsync(
            new IssueTokenRequest
            {
                Audience = "nof-tests",
                ClientId = string.Empty,
                AccessTokenExpiration = TimeSpan.FromMinutes(5),
                AccessClaims =
                [
                    new(JwtRegisteredClaimNames.Sub, "user-1")
                ]
            },
            CancellationToken.None);

        Assert.NotNull(generateResult.ExpectFailure());
        Assert.Equal("400", generateResult.ExpectFailure().Code);
    }

    [Fact]
    public async Task ValidateRefreshToken_ShouldFail_WhenAudienceDoesNotMatch()
    {
        var builder = CreateOidcServerBuilder();

        await using var host = await builder.BuildTestHostAsync();
        using var scope = host.CreateScope();
        var tokenService = scope.GetRequiredService<ITokenService>();

        var generateResult = await tokenService.IssueTokenAsync(
            new IssueTokenRequest
            {
                Audience = "nof-tests",
                ClientId = "web-client",
                AccessTokenExpiration = TimeSpan.FromMinutes(5),
                AccessClaims = [new(JwtRegisteredClaimNames.Sub, "user-1")],
                RefreshToken = new RefreshTokenOptions
                {
                    Expiration = TimeSpan.FromMinutes(10),
                    Claims = [new(JwtRegisteredClaimNames.Sub, "user-1")]
                }
            },
            CancellationToken.None);

        Assert.NotNull(generateResult.ExpectSuccess());
        Assert.NotNull(generateResult.ExpectSuccess().RefreshToken);

        var validateResult = await tokenService.ValidateRefreshTokenAsync(
            new ValidateRefreshTokenRequest
            {
                RefreshToken = generateResult.ExpectSuccess().RefreshToken!.Token,
                Audience = "other-audience"
            },
            CancellationToken.None);

        Assert.NotNull(validateResult.ExpectFailure());
    }

    [Fact]
    public async Task IntrospectToken_ShouldReturnActiveAccessToken()
    {
        var builder = CreateOidcServerBuilder();

        await using var host = await builder.BuildTestHostAsync();
        using var scope = host.CreateScope();
        var tokenService = scope.GetRequiredService<ITokenService>();

        var generateResult = await tokenService.IssueTokenAsync(
            new IssueTokenRequest
            {
                Audience = "nof-tests",
                ClientId = "service-a",
                AccessTokenExpiration = TimeSpan.FromMinutes(5),
                AccessClaims =
                [
                    new(JwtRegisteredClaimNames.Sub, "user-1"),
                    new(OAuthClaimTypes.Scope, "jobs.read")
                ]
            },
            CancellationToken.None);

        Assert.NotNull(generateResult.ExpectSuccess());

        var introspectResult = await tokenService.IntrospectTokenAsync(
            new IntrospectTokenRequest
            {
                Token = generateResult.ExpectSuccess().AccessToken,
                TokenTypeHint = OAuthTokenTypes.AccessToken,
                Audience = "nof-tests"
            },
            CancellationToken.None);

        Assert.NotNull(introspectResult.ExpectSuccess());
        Assert.True(introspectResult.ExpectSuccess().Active);
        Assert.Equal(OAuthTokenTypes.AccessToken, introspectResult.ExpectSuccess().TokenType);
        Assert.Contains(introspectResult.ExpectSuccess().Claims, claim => claim.Type == JwtRegisteredClaimNames.Sub && claim.Value == "user-1");
        Assert.Contains(introspectResult.ExpectSuccess().Claims, claim => claim.Type == OAuthClaimTypes.ClientId && claim.Value == "service-a");
        Assert.Contains(introspectResult.ExpectSuccess().Claims, claim => claim.Type == OAuthClaimTypes.Scope && claim.Value == "jobs.read");
    }

    [Fact]
    public async Task IntrospectToken_ShouldReturnExplicitArrayClaimsAsSingleMultiValueClaim()
    {
        var builder = CreateOidcServerBuilder();

        await using var host = await builder.BuildTestHostAsync();
        using var scope = host.CreateScope();
        var tokenService = scope.GetRequiredService<ITokenService>();

        var generateResult = await tokenService.IssueTokenAsync(
            new IssueTokenRequest
            {
                Audience = "nof-tests",
                ClientId = "web-client",
                AccessTokenExpiration = TimeSpan.FromMinutes(5),
                AccessClaims =
                [
                    new(JwtRegisteredClaimNames.Sub, "user-1"),
                    TokenClaim.Array(OAuthClaimTypes.Groups, "engineering", "ops")
                ]
            },
            CancellationToken.None);

        Assert.NotNull(generateResult.ExpectSuccess());

        var introspectResult = await tokenService.IntrospectTokenAsync(
            new IntrospectTokenRequest
            {
                Token = generateResult.ExpectSuccess().AccessToken,
                TokenTypeHint = OAuthTokenTypes.AccessToken,
                Audience = "nof-tests"
            },
            CancellationToken.None);

        Assert.NotNull(introspectResult.ExpectSuccess());
        Assert.True(introspectResult.ExpectSuccess().Active);

        var groupsClaim = Assert.Single(introspectResult.ExpectSuccess().Claims, static claim => claim.Type == OAuthClaimTypes.Groups);
        Assert.Null(groupsClaim.Value);
        Assert.NotNull(groupsClaim.Values);
        Assert.Equal(["engineering", "ops"], groupsClaim.Values);
    }

    [Fact]
    public async Task IntrospectToken_ShouldReturnInactiveForRevokedRefreshToken()
    {
        var builder = CreateOidcServerBuilder();

        await using var host = await builder.BuildTestHostAsync();
        using var scope = host.CreateScope();
        var tokenService = scope.GetRequiredService<ITokenService>();

        var generateResult = await tokenService.IssueTokenAsync(
            new IssueTokenRequest
            {
                Audience = "nof-tests",
                ClientId = "public-app",
                AccessTokenExpiration = TimeSpan.FromMinutes(5),
                AccessClaims = [new(JwtRegisteredClaimNames.Sub, "user-1")],
                RefreshToken = new RefreshTokenOptions
                {
                    Expiration = TimeSpan.FromMinutes(10),
                    Claims =
                    [
                        new(JwtRegisteredClaimNames.Sub, "user-1")
                    ]
                }
            },
            CancellationToken.None);

        Assert.NotNull(generateResult.ExpectSuccess());
        Assert.NotNull(generateResult.ExpectSuccess().RefreshToken);

        var validateResult = await tokenService.ValidateRefreshTokenAsync(
            new ValidateRefreshTokenRequest
            {
                RefreshToken = generateResult.ExpectSuccess().RefreshToken!.Token,
                Audience = "nof-tests"
            },
            CancellationToken.None);

        Assert.NotNull(validateResult.ExpectSuccess());

        var revokeResult = await tokenService.RevokeRefreshTokenAsync(
            new RevokeRefreshTokenRequest
            {
                TokenId = validateResult.ExpectSuccess().TokenId,
                Expiration = TimeSpan.FromMinutes(10)
            },
            CancellationToken.None);

        Assert.NotNull(revokeResult.ExpectSuccess());

        var introspectResult = await tokenService.IntrospectTokenAsync(
            new IntrospectTokenRequest
            {
                Token = generateResult.ExpectSuccess().RefreshToken!.Token,
                TokenTypeHint = OAuthTokenTypes.RefreshToken,
                Audience = "nof-tests"
            },
            CancellationToken.None);

        Assert.NotNull(introspectResult.ExpectSuccess());
        Assert.False(introspectResult.ExpectSuccess().Active);
        Assert.Equal(OAuthTokenTypes.RefreshToken, introspectResult.ExpectSuccess().TokenType);
    }

    private static byte[] Base64UrlDecode(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        padded = padded.PadRight(padded.Length + ((4 - padded.Length % 4) % 4), '=');
        return Convert.FromBase64String(padded);
    }

    private static NOFTestAppBuilder CreateOidcServerBuilder()
    {
        var builder = NOFTestAppBuilder.Create();
        builder.AddOidcServer(options =>
        {
            options.Issuer = "https://issuer.local";
            options.SigningKeyEncryptionKey = SigningKeyEncryptionKey;
        });
        builder.UseDbContext<NOFDbContext>()
            .WithConnectionString($"Data Source=nof-jwt-tests-{Guid.NewGuid():N}-{{tenantId}};Mode=Memory;Cache=Shared")
            .WithOptions(static (optionsBuilder, databaseConnectionString) => optionsBuilder.UseSqlite(databaseConnectionString));

        return builder;
    }
}
