using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NOF.Sample.Identity;
using NOF.Sample.Services;
using NOF.Sample.UI.Services;
using OpenIddict.Abstractions;
using OpenIddict.Server;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Xunit;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace NOF.Sample.Tests;

public sealed class NativeOidcTests
{
    [Fact]
    public async Task NativeDiscovery_ClientCredentials_Revocation_AndJwtBearerWorkTogether()
    {
        await using var fixture = await Fixture.StartAsync();
        using var discovery = JsonDocument.Parse(await fixture.Client.GetStringAsync("/oauth2/.well-known/openid-configuration"));
        Assert.Equal("http://localhost/oauth2/", discovery.RootElement.GetProperty("issuer").GetString());
        Assert.Contains(GrantTypes.TokenExchange, discovery.RootElement.GetProperty("grant_types_supported").EnumerateArray().Select(item => item.GetString()));
        using var keys = JsonDocument.Parse(await fixture.Client.GetStringAsync("/oauth2/.well-known/jwks.json"));
        Assert.NotEmpty(keys.RootElement.GetProperty("keys").EnumerateArray());
        Assert.All(keys.RootElement.GetProperty("keys").EnumerateArray(), key => Assert.False(key.TryGetProperty("d", out _)));
        using var anonymous = await fixture.Client.GetAsync("/protected");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.Contains(anonymous.Headers.WwwAuthenticate, item => item.Scheme == "Bearer");
        var token = await fixture.ClientTokenAsync();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/protected");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var accepted = await fixture.Client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        var introspected = await fixture.PostAsync("/oauth2/introspect", new() { ["token"] = token, ["client_id"] = "machine", ["client_secret"] = Fixture.Secret });
        Assert.True(introspected.GetProperty("active").GetBoolean());
        await fixture.PostAsync("/oauth2/revoke", new() { ["token"] = token, ["client_id"] = "machine", ["client_secret"] = Fixture.Secret });
        var revoked = await fixture.PostAsync("/oauth2/introspect", new() { ["token"] = token, ["client_id"] = "machine", ["client_secret"] = Fixture.Secret });
        Assert.False(revoked.GetProperty("active").GetBoolean());
        var rejected = await fixture.PostAsync("/oauth2/token", new() { ["grant_type"] = GrantTypes.ClientCredentials, ["client_id"] = "machine", ["client_secret"] = "wrong" }, false);
        Assert.Equal(Errors.InvalidClient, rejected.GetProperty("error").GetString());
    }

    [Fact]
    public async Task NativeAuthorizationCode_Pkce_UserInfo_Refresh_AndReplayProtectionWork()
    {
        await using var fixture = await Fixture.StartAsync();
        var verifier = Base64UrlEncoder(RandomNumberGenerator.GetBytes(32));
        var code = await fixture.CodeAsync(verifier);
        Dictionary<string, string> Grant(string proof) => new()
        {
            ["grant_type"] = GrantTypes.AuthorizationCode,
            ["client_id"] = "nof-sample-ui",
            ["code"] = code,
            ["redirect_uri"] = "https://client.local/oauth/callback",
            ["code_verifier"] = proof
        };
        var badProof = await fixture.PostAsync("/oauth2/token", Grant("invalid-proof-" + verifier), false);
        Assert.Equal(Errors.InvalidGrant, badProof.GetProperty("error").GetString());
        var code2 = await fixture.CodeAsync(verifier);
        code = code2;
        var granted = await fixture.PostAsync("/oauth2/token", Grant(verifier));
        Assert.True(granted.TryGetProperty("id_token", out _));
        using var info = new HttpRequestMessage(HttpMethod.Get, "/oauth2/userinfo");
        info.Headers.Authorization = new("Bearer", granted.GetProperty("access_token").GetString());
        using var profile = await fixture.Client.SendAsync(info);
        Assert.True(profile.IsSuccessStatusCode, await profile.Content.ReadAsStringAsync());
        using var claims = JsonDocument.Parse(await profile.Content.ReadAsStringAsync());
        Assert.Equal("demo-user", claims.RootElement.GetProperty("sub").GetString());
        Assert.Equal("sample.user@nof.local", claims.RootElement.GetProperty("email").GetString());
        var refreshed = await fixture.PostAsync("/oauth2/token", new()
        {
            ["grant_type"] = GrantTypes.RefreshToken,
            ["client_id"] = "nof-sample-ui",
            ["refresh_token"] = granted.GetProperty("refresh_token").GetString()!
        });
        Assert.True(refreshed.TryGetProperty("access_token", out _));
        var replay = await fixture.PostAsync("/oauth2/token", Grant(verifier), false);
        Assert.Equal(Errors.InvalidGrant, replay.GetProperty("error").GetString());
    }

    [Fact]
    public async Task NativeExchange_KeepsSubjectAndActor_AndRejectsScopeEscalation()
    {
        await using var fixture = await Fixture.StartAsync();
        var verifier = Base64UrlEncoder(RandomNumberGenerator.GetBytes(32));
        var code = await fixture.CodeAsync(verifier, "machine");
        var user = await fixture.PostAsync("/oauth2/token", new()
        {
            ["grant_type"] = GrantTypes.AuthorizationCode,
            ["client_id"] = "nof-sample-ui",
            ["code"] = code,
            ["redirect_uri"] = "https://client.local/oauth/callback",
            ["code_verifier"] = verifier
        });
        var actor = await fixture.ClientTokenAsync("sample.read");
        Dictionary<string, string> Exchange(string scope) => new()
        {
            ["grant_type"] = GrantTypes.TokenExchange,
            ["client_id"] = "machine",
            ["client_secret"] = Fixture.Secret,
            ["subject_token"] = user.GetProperty("access_token").GetString()!,
            ["subject_token_type"] = TokenTypeIdentifiers.AccessToken,
            ["actor_token"] = actor,
            ["actor_token_type"] = TokenTypeIdentifiers.AccessToken,
            ["requested_token_type"] = TokenTypeIdentifiers.AccessToken,
            ["scope"] = scope
        };
        var delegated = await fixture.PostAsync("/oauth2/token", Exchange("sample.read"));
        using var call = new HttpRequestMessage(HttpMethod.Get, "/protected");
        call.Headers.Authorization = new("Bearer", delegated.GetProperty("access_token").GetString());
        using var response = await fixture.Client.SendAsync(call);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var principal = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("demo-user", principal.RootElement.GetProperty("subject").GetString());
        using var actorClaim = JsonDocument.Parse(principal.RootElement.GetProperty("actor").GetString()!);
        Assert.Equal("machine", actorClaim.RootElement.GetProperty("sub").GetString());
        var wrongActor = Exchange("sample.read");
        wrongActor["actor_token"] = user.GetProperty("access_token").GetString()!;
        var impersonation = await fixture.PostAsync("/oauth2/token", wrongActor, false);
        Assert.Equal(Errors.InvalidGrant, impersonation.GetProperty("error").GetString());
        var wider = await fixture.PostAsync("/oauth2/token", Exchange("sample.write"), false);
        Assert.Equal(Errors.InvalidGrant, wider.GetProperty("error").GetString());
        var excessive = await fixture.PostAsync("/oauth2/token", Exchange("sample.admin"), false);
        Assert.True(excessive.TryGetProperty("error", out _));
    }

    [Fact]
    public async Task NativeDeviceAuthorization_RequiresApproval_AndProducesAnAccessToken()
    {
        await using var fixture = await Fixture.StartAsync();
        var device = await fixture.PostAsync("/oauth2/device_authorization", new() { ["client_id"] = "nof-sample-ui", ["scope"] = "sample.read" });
        var code = device.GetProperty("device_code").GetString()!;
        Dictionary<string, string> Poll() => new() { ["grant_type"] = GrantTypes.DeviceCode, ["client_id"] = "nof-sample-ui", ["device_code"] = code };
        var pending = await fixture.PostAsync("/oauth2/token", Poll(), false);
        Assert.Equal(Errors.AuthorizationPending, pending.GetProperty("error").GetString());
        using var approval = await fixture.Client.PostAsync("/oauth2/device", new FormUrlEncodedContent(new Dictionary<string, string> { ["user_code"] = device.GetProperty("user_code").GetString()! }));
        Assert.True(approval.IsSuccessStatusCode || approval.StatusCode == HttpStatusCode.Redirect, await approval.Content.ReadAsStringAsync());
        var approved = await fixture.PostAsync("/oauth2/token", Poll());
        Assert.True(approved.TryGetProperty("access_token", out _));
    }

    [Fact]
    public async Task DemoBackend_UsesNativeClientManagementAndHttpForTheCompleteDelegationFlow()
    {
        await using var fixture = await Fixture.StartAsync();
        await using var scope = fixture.Services.CreateAsyncScope();
        var backend = new OAuthChainDemoBackend(fixture.Client,
            scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>(),
            fixture.Services.GetRequiredService<IOptions<OpenIddictServerOptions>>());
        Assert.IsType<DemoTokenResponse>((await backend.GetClientTokenAsync(new()
        { ClientId = "machine", ClientSecret = Fixture.Secret }, default)).Value);
        var invalid = await backend.CreateClientAsync(new() { ClientIdPrefix = new string('a', 101) }, default);
        Assert.Equal(HttpStatusCode.BadRequest, Assert.IsType<HttpTransportFailure>(invalid.Value).StatusCode);
        var created = Assert.IsType<CreateDemoOAuthClientResponse>((await backend.CreateClientAsync(new(), default)).Value);
        var actor = Assert.IsType<DemoTokenResponse>((await backend.GetClientTokenAsync(new()
        { ClientId = created.ClientId, ClientSecret = created.ClientSecret }, default)).Value);
        var user = Assert.IsType<DemoTokenResponse>((await backend.GetUserTokenAsync(new()
        { RecipientClientId = created.ClientId }, default)).Value);
        var exchanged = Assert.IsType<DemoTokenResponse>((await backend.ExchangeTokenAsync(new()
        {
            ClientId = created.ClientId,
            ClientSecret = created.ClientSecret,
            ClientAccessToken = actor.AccessToken,
            UserAccessToken = user.AccessToken,
            RequestedScope = "sample.read"
        }, default)).Value);
        using var request = new HttpRequestMessage(HttpMethod.Get, "/protected");
        request.Headers.Authorization = new("Bearer", exchanged.AccessToken);
        using var response = await fixture.Client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var claims = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("demo-user", claims.RootElement.GetProperty("subject").GetString());
    }

    private static string Base64UrlEncoder(byte[] data) => Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    public sealed class IdentityTestContext(DbContextOptions<IdentityTestContext> options) : DbContext(options);
    public sealed class Fixture(WebApplication app, SqliteConnection connection, HttpClient client) : IAsyncDisposable
    {
        public const string Secret = "integration+only/secret=with:credentials-and-adequate-entropy";
        public HttpClient Client { get; } = client;
        public IServiceProvider Services => app.Services;
        public static async Task<Fixture> StartAsync()
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var builder = WebApplication.CreateBuilder([]);
            builder.WebHost.UseTestServer();
            builder.Services.AddDbContext<IdentityTestContext>(options => options.UseSqlite(connection).UseOpenIddict());
            var issuer = new Uri("http://localhost/oauth2/");
            builder.Services.AddSampleIdentity<IdentityTestContext>(issuer, true, false,
                options => options.AddEphemeralEncryptionKey().AddEphemeralSigningKey());
            WebApplication? app = null;
            builder.Services.Configure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options => options.Backchannel = app!.GetTestClient());
            app = builder.Build();
            app.UseAuthentication();
            app.UseAuthorization();
            app.MapSampleIdentity(true);
            app.MapGet("/protected", (Microsoft.AspNetCore.Http.HttpContext http) => new
            {
                subject = http.User.FindFirst(Claims.Subject)?.Value,
                actor = http.User.FindFirst(Claims.Actor)?.Value
            }).RequireAuthorization();
            await using (var scope = app.Services.CreateAsyncScope())
            {
                await scope.ServiceProvider.GetRequiredService<IdentityTestContext>().Database.EnsureCreatedAsync();
                var manager = scope.ServiceProvider.GetRequiredService<IOpenIddictApplicationManager>();
                await SampleIdentityBootstrap.SeedClientsAsync(manager, issuer);
                await manager.CreateAsync(new OpenIddictApplicationDescriptor
                {
                    ClientId = "machine",
                    ClientSecret = Secret,
                    ClientType = ClientTypes.Confidential,
                    Permissions =
                    {
                        Permissions.Endpoints.Token, Permissions.Endpoints.Introspection, Permissions.Endpoints.Revocation,
                        Permissions.GrantTypes.ClientCredentials, Permissions.GrantTypes.TokenExchange,
                        Permissions.Prefixes.Scope + "sample.read", Permissions.Prefixes.Scope + "sample.write"
                    }
                });
            }
            await app.StartAsync();
            return new(app, connection, app.GetTestClient());
        }

        public async Task<JsonElement> PostAsync(string path, Dictionary<string, string> form, bool success = true)
        {
            using var response = await Client.PostAsync(path, new FormUrlEncodedContent(form));
            var body = await response.Content.ReadAsStringAsync();
            Assert.True(success == response.IsSuccessStatusCode, body);
            using var json = JsonDocument.Parse(body);
            return json.RootElement.Clone();
        }
        public async Task<string> ClientTokenAsync(string scope = "sample.read sample.write")
        {
            var token = await PostAsync("/oauth2/token", new() { ["grant_type"] = GrantTypes.ClientCredentials, ["client_id"] = "machine", ["client_secret"] = Secret, ["scope"] = scope });
            return token.GetProperty("access_token").GetString()!;
        }
        public async Task<string> CodeAsync(string verifier, string? recipient = null)
        {
            var challenge = Base64UrlEncoder(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
            var url = "/oauth2/authorize?response_type=code&client_id=nof-sample-ui&redirect_uri=" + Uri.EscapeDataString("https://client.local/oauth/callback")
                + "&scope=" + Uri.EscapeDataString("openid profile email offline_access sample.read sample.write") + "&state=roundtrip&nonce=identity-test&code_challenge_method=S256&code_challenge=" + challenge;
            if (recipient is not null)
            {
                url += "&delegate_to=" + Uri.EscapeDataString(recipient);
            }
            using var response = await Client.GetAsync(url);
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
            var query = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(response.Headers.Location!.Query);
            Assert.Equal("roundtrip", query["state"].ToString());
            return query["code"].ToString();
        }
        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            await app.DisposeAsync();
            await connection.DisposeAsync();
        }
    }
}
