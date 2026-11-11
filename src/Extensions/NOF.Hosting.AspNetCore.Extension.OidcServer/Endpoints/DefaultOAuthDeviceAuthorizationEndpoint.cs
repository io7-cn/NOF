using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using NOF.Contract;
using OidcRoutes = Microsoft.AspNetCore.Routing.NOFOidcServerExtensions;

namespace NOF.Hosting.AspNetCore.Extension.OidcServer;

public sealed class DefaultOAuthDeviceAuthorizationEndpoint(
    IServiceProvider serviceProvider,
    IOAuthClientRepository clientRepository,
    IOAuthDeviceGrantService deviceGrantService,
    IOptions<OAuthAuthorizationServerOptions> options) : IOAuthDeviceAuthorizationEndpoint
{
    public async Task<IResult> HandleAsync(
        OAuthDeviceAuthorizationEndpointRequest request,
        CancellationToken cancellationToken)
    {
        var httpRequest = request.HttpRequest;
        var serverOptions = options.Value;
        if (serverOptions.RequireHttpsForDeviceAuthorization && !httpRequest.IsHttps)
        {
            return CreateError(httpRequest, "invalid_request", "device authorization requires HTTPS.");
        }

        var tokenRequest = new OAuthTokenRequest
        {
            GrantType = OAuthGrantTypes.DeviceCode,
            ClientId = request.Request.ClientId,
            ClientSecret = request.Request.ClientSecret,
            ClientAssertionType = request.Request.ClientAssertionType,
            ClientAssertion = request.Request.ClientAssertion,
            Scope = OidcRoutes.NormalizeScope(request.Request.Scope)
        };
        OidcRoutes.ApplyResolvedClientCredentials(httpRequest, tokenRequest);
        var authenticationError = await OidcRoutes.ValidateClientAuthenticationAsync(
            httpRequest,
            tokenRequest,
            serviceProvider,
            cancellationToken,
            $"{serverOptions.Issuer.TrimEnd('/')}/device_authorization").ConfigureAwait(false);
        if (authenticationError is not null)
        {
            return CreateError(httpRequest, authenticationError.Error, authenticationError.ErrorDescription);
        }

        var clientResult = await clientRepository.GetAsync(tokenRequest.ClientId, cancellationToken).ConfigureAwait(false);
        if (clientResult is not Success<OAuthClientDescriptor> clientResultSuccess || !clientResultSuccess.Data.IsEnabled)
        {
            return CreateError(httpRequest, "invalid_client", "client credentials are invalid.");
        }

        var requestedScopes = OidcRoutes.ParseScopes(tokenRequest.Scope);
        if (requestedScopes.Any(scope => !clientResultSuccess.Data.AllowedScopes.Contains(scope, StringComparer.Ordinal)))
        {
            return CreateError(httpRequest, "invalid_scope", "requested scope is not allowed for this client.");
        }

        var createResult = await deviceGrantService.CreateAsync(
            new CreateOAuthDeviceGrantRequest
            {
                ClientId = clientResultSuccess.Data.ClientId,
                ClientDisplayName = clientResultSuccess.Data.DisplayName,
                ClientLogoUri = clientResultSuccess.Data.LogoUri,
                Scopes = requestedScopes
            },
            cancellationToken).ConfigureAwait(false);
        SetNoStore(httpRequest);
        return createResult switch
        {
            Success<OAuthDeviceAuthorizationResponse> success => Results.Json(success.Data),
            Failure failure => CreateError(httpRequest, failure.Code, failure.Message),
            _ => throw new InvalidOperationException("The response contains no outcome case.")
        };
    }

    private static IResult CreateError(HttpRequest request, string error, string description)
    {
        SetNoStore(request);
        return OidcRoutes.CreateOAuthErrorResult(error, description);
    }

    private static void SetNoStore(HttpRequest request)
    {
        request.HttpContext.Response.Headers.CacheControl = "no-store";
        request.HttpContext.Response.Headers.Pragma = "no-cache";
    }
}
