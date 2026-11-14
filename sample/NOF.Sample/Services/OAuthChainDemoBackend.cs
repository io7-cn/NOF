using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Options;
using NOF.Abstraction;
using NOF.Contract;
using NOF.Sample.UI.Services;
using OpenIddict.Abstractions;
using OpenIddict.Server;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace NOF.Sample.Services;

public sealed class OAuthChainDemoBackend(
    HttpClient httpClient,
    IOpenIddictApplicationManager clientManager,
    IOptions<OpenIddictServerOptions> authorizationServerOptions)
{
    public union ClientReply(CreateDemoOAuthClientResponse, HttpTransportFailure);
    public union TokenReply(DemoTokenResponse, HttpTransportFailure);

    private static readonly string[] AllowedScopes = ["sample.read", "sample.write"];
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private string Issuer => authorizationServerOptions.Value.Issuer!.AbsoluteUri.TrimEnd('/');
    private string TokenEndpoint => $"{Issuer}/token";
    private string AuthorizeEndpoint => $"{Issuer}/authorize";

    public async Task<ClientReply> CreateClientAsync(
        CreateDemoOAuthClientRequest request,
        CancellationToken cancellationToken)
    {
        var clientIdPrefix = string.IsNullOrWhiteSpace(request.ClientIdPrefix)
            ? "sample-client"
            : request.ClientIdPrefix.Trim().ToLowerInvariant();
        if (clientIdPrefix.Length > 64 || clientIdPrefix.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not '-' and not '_'))
        {
            return new HttpTransportFailure(HttpStatusCode.BadRequest, "Invalid client prefix", "Use at most 64 ASCII letters, digits, hyphens or underscores.");
        }
        var clientId = $"{clientIdPrefix}-{Guid.NewGuid().ToString("N")[..8]}";
        var secret = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        await clientManager.CreateAsync(new OpenIddictApplicationDescriptor
        {
            ClientId = clientId,
            ClientSecret = secret,
            ClientType = ClientTypes.Confidential,
            DisplayName = $"Sample Demo Client {clientId}",
            Permissions =
            {
                Permissions.Endpoints.Token, Permissions.Endpoints.Introspection, Permissions.Endpoints.Revocation,
                Permissions.GrantTypes.ClientCredentials, Permissions.GrantTypes.TokenExchange,
                Permissions.Prefixes.Scope + "sample.read", Permissions.Prefixes.Scope + "sample.write"
            }
        }, cancellationToken);

        return new CreateDemoOAuthClientResponse
        {
            ClientId = clientId,
            ClientSecret = secret,
            AllowedScopes = AllowedScopes
        };
    }

    public async Task<TokenReply> GetClientTokenAsync(
        GetDemoClientTokenRequest request,
        CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, TokenEndpoint)
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["scope"] = string.Join(' ', AllowedScopes)
            })
        };
        message.Headers.Authorization = CreateBasicAuthorizationHeader(request.ClientId, request.ClientSecret);

        return await SendTokenRequestAsync(message, cancellationToken).ConfigureAwait(false);
    }

    public async Task<TokenReply> GetUserTokenAsync(GetDemoUserTokenRequest request, CancellationToken cancellationToken)
    {
        var codeVerifier = CreateCodeVerifier();
        var codeChallenge = Base64UrlEncode(SHA256.HashData(Encoding.ASCII.GetBytes(codeVerifier)));
        var redirectUri = "https://client.local/oauth/callback";
        var state = $"state-{Guid.NewGuid():N}";
        var authorizeUrl =
            $"{AuthorizeEndpoint}?response_type=code" +
            "&client_id=nof-sample-ui" +
            $"&redirect_uri={Uri.EscapeDataString(redirectUri)}" +
            $"&scope={Uri.EscapeDataString("openid profile email sample.read sample.write")}" +
            $"&state={Uri.EscapeDataString(state)}" +
            $"&nonce={Uri.EscapeDataString($"nonce-{Guid.NewGuid().ToString("N")[..8]}")}" +
            $"&code_challenge={Uri.EscapeDataString(codeChallenge)}" +
            "&code_challenge_method=S256";

        if (!string.IsNullOrWhiteSpace(request.RecipientClientId))
        {
            authorizeUrl += "&delegate_to=" + Uri.EscapeDataString(request.RecipientClientId);
        }
        using var authorizeRequest = new HttpRequestMessage(HttpMethod.Get, authorizeUrl);
        using var authorizeResponse = await httpClient.SendAsync(
            authorizeRequest,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken).ConfigureAwait(false);

        if (authorizeResponse.StatusCode != HttpStatusCode.Redirect
            && authorizeResponse.StatusCode != HttpStatusCode.Found)
        {
            return await CreateFailureAsync(authorizeResponse, cancellationToken).ConfigureAwait(false);
        }

        var redirectLocation = authorizeResponse.Headers.Location?.ToString();
        if (string.IsNullOrWhiteSpace(redirectLocation))
        {
            return new HttpTransportFailure(HttpStatusCode.InternalServerError, "Invalid OAuth response", "Authorization response did not contain redirect location.");
        }

        var callback = QueryHelpers.ParseQuery(new Uri(redirectLocation, UriKind.Absolute).Query);
        if (!callback.TryGetValue("state", out var returnedState) || returnedState != state)
        {
            return new HttpTransportFailure(HttpStatusCode.BadGateway, "Invalid OAuth response", "Authorization state did not match the request.");
        }
        if (callback.TryGetValue("error", out var error) && !string.IsNullOrEmpty(error))
        {
            return new HttpTransportFailure(HttpStatusCode.BadRequest, "Authorization rejected", callback.TryGetValue("error_description", out var description) ? description.ToString() : error.ToString());
        }
        var code = callback.TryGetValue("code", out var returnedCode) ? returnedCode.ToString() : null;
        if (string.IsNullOrEmpty(code))
        {
            return new HttpTransportFailure(HttpStatusCode.BadGateway, "Invalid OAuth response", "Authorization response contained no code.");
        }
        using var tokenRequest = new HttpRequestMessage(HttpMethod.Post, TokenEndpoint)
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["client_id"] = "nof-sample-ui",
                ["code"] = code,
                ["redirect_uri"] = redirectUri,
                ["code_verifier"] = codeVerifier
            })
        };

        return await SendTokenRequestAsync(tokenRequest, cancellationToken).ConfigureAwait(false);
    }

    public async Task<TokenReply> ExchangeTokenAsync(
        ExchangeDemoTokenRequest request,
        CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, TokenEndpoint)
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "urn:ietf:params:oauth:grant-type:token-exchange",
                ["subject_token"] = request.UserAccessToken,
                ["subject_token_type"] = "urn:ietf:params:oauth:token-type:access_token",
                ["actor_token"] = request.ClientAccessToken,
                ["actor_token_type"] = "urn:ietf:params:oauth:token-type:access_token",
                ["requested_token_type"] = "urn:ietf:params:oauth:token-type:access_token",
                ["scope"] = request.RequestedScope
            })
        };
        message.Headers.Authorization = CreateBasicAuthorizationHeader(request.ClientId, request.ClientSecret);

        return await SendTokenRequestAsync(message, cancellationToken).ConfigureAwait(false);
    }

    private async Task<TokenReply> SendTokenRequestAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        using var response = await httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            return await CreateFailureAsync(response, cancellationToken).ConfigureAwait(false);
        }

        var payload = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        var token = JsonSerializer.Deserialize<OAuthTokenResponseDocument>(payload, JsonOptions);
        if (token?.AccessToken is null)
        {
            return new HttpTransportFailure(HttpStatusCode.InternalServerError, "Invalid OAuth response", "Token response payload is invalid.");
        }

        return new DemoTokenResponse
        {
            AccessToken = token.AccessToken,
            TokenType = string.IsNullOrWhiteSpace(token.TokenType) ? "Bearer" : token.TokenType,
            ExpiresIn = token.ExpiresIn,
            Scope = token.Scope,
            IdToken = token.IdToken
        };
    }

    private static async Task<HttpTransportFailure> CreateFailureAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var payload = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        return new HttpTransportFailure(response.StatusCode, response.ReasonPhrase ?? "OAuth request failed", string.IsNullOrWhiteSpace(payload) ? response.ReasonPhrase ?? "Request failed." : payload);
    }

    private static AuthenticationHeaderValue CreateBasicAuthorizationHeader(string clientId, string clientSecret)
        => new("Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{Uri.EscapeDataString(clientId)}:{Uri.EscapeDataString(clientSecret)}")));

    private static string CreateCodeVerifier()
    {
        Span<byte> buffer = stackalloc byte[32];
        RandomNumberGenerator.Fill(buffer);
        return Base64UrlEncode(buffer.ToArray());
    }

    private static string Base64UrlEncode(byte[] value)
        => Convert.ToBase64String(value)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    private sealed record OAuthTokenResponseDocument
    {
        [JsonPropertyName("access_token")]
        public string? AccessToken { get; init; }

        [JsonPropertyName("token_type")]
        public string? TokenType { get; init; }

        [JsonPropertyName("expires_in")]
        public long? ExpiresIn { get; init; }

        [JsonPropertyName("scope")]
        public string? Scope { get; init; }

        [JsonPropertyName("id_token")]
        public string? IdToken { get; init; }
    }
}
