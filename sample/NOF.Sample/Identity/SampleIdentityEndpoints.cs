using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace NOF.Sample.Identity;

/// <summary>Supplies demo user decisions; OpenIddict owns validation, credentials and protocol responses.</summary>
public static class SampleIdentityEndpoints
{
    public static IEndpointRouteBuilder MapSampleIdentity(this IEndpointRouteBuilder endpoints, bool development)
    {
        endpoints.MapMethods("/oauth2/authorize", ["GET", "POST"], async (HttpContext http, IOpenIddictApplicationManager clients) =>
        {
            // This fixed-user decision is a demonstration only, never an authentication mechanism for deployment.
            if (!development)
            {
                return Results.Forbid(authenticationSchemes: [OpenIddictServerAspNetCoreDefaults.AuthenticationScheme]);
            }
            var request = http.GetOpenIddictServerRequest()!;
            var principal = SampleIdentityConfiguration.CreatePrincipal("demo-user", request.GetScopes());
            var recipient = (string?)request["delegate_to"];
            if (!string.IsNullOrWhiteSpace(recipient))
            {
                // Application consent: only a registered exchange client may receive this demo user's delegation.
                var client = await clients.FindByClientIdAsync(recipient, http.RequestAborted);
                if (client is null || !await clients.HasPermissionAsync(client, Permissions.GrantTypes.TokenExchange, http.RequestAborted))
                {
                    return Results.Forbid(authenticationSchemes: [OpenIddictServerAspNetCoreDefaults.AuthenticationScheme]);
                }
                principal.SetResources("nof-sample", recipient);
            }
            return Results.SignIn(principal,
                authenticationScheme: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        });
        endpoints.MapMethods("/oauth2/device", ["GET", "POST"], async (HttpContext http) =>
        {
            if (!development || !HttpMethods.IsPost(http.Request.Method))
            {
                return Results.BadRequest(new { error = "invalid_request", error_description = "POST the user_code to approve the development demo user." });
            }
            var ticket = await http.AuthenticateAsync(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
            if (ticket.Principal is null)
            {
                return Results.BadRequest(new { error = "invalid_grant" });
            }
            var principal = SampleIdentityConfiguration.CreatePrincipal("demo-user", ticket.Principal.GetScopes());
            return Results.SignIn(principal, authenticationScheme: OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
        });
        endpoints.MapGet("/oauth2/userinfo", async (HttpContext http) =>
        {
            var ticket = await http.AuthenticateAsync(Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerDefaults.AuthenticationScheme);
            if (!ticket.Succeeded)
            {
                return Results.Challenge(authenticationSchemes: [Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerDefaults.AuthenticationScheme]);
            }
            var principal = ticket.Principal!;
            var scopes = principal.FindAll(Claims.Scope).SelectMany(claim => claim.Value.Split(' ')).ToHashSet(StringComparer.Ordinal);
            return Results.Json(new Dictionary<string, object?>
            {
                [Claims.Subject] = principal.GetClaim(Claims.Subject),
                [Claims.Name] = scopes.Contains(Scopes.Profile) ? principal.GetClaim(Claims.Name) : null,
                [Claims.Email] = scopes.Contains(Scopes.Email) ? principal.GetClaim(Claims.Email) : null
            });
        });
        return endpoints;
    }
}
