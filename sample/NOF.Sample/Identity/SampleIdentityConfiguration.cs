using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OpenIddict.Abstractions;
using OpenIddict.Server;
using OpenIddict.Server.AspNetCore;
using System.Security.Claims;
using System.Text.Json;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace NOF.Sample.Identity;

/// <summary>Application-owned identity composition using native OpenIddict and JwtBearer APIs.</summary>
public static class SampleIdentityConfiguration
{
    public static IServiceCollection AddSampleIdentity<TContext>(this IServiceCollection services, Uri issuer,
        bool development, bool initializeClients = true, Action<OpenIddictServerBuilder>? credentials = null) where TContext : DbContext
    {
        services.AddOpenIddict()
            .AddCore(options => options.UseEntityFrameworkCore().UseDbContext<TContext>())
            .AddServer(options =>
            {
                options.SetIssuer(issuer);
                options.SetConfigurationEndpointUris("/oauth2/.well-known/openid-configuration", "/.well-known/oauth-authorization-server/oauth2");
                options.SetJsonWebKeySetEndpointUris("/oauth2/.well-known/jwks.json");
                options.SetAuthorizationEndpointUris("/oauth2/authorize");
                options.SetTokenEndpointUris("/oauth2/token");
                options.SetUserInfoEndpointUris("/oauth2/userinfo");
                options.SetIntrospectionEndpointUris("/oauth2/introspect");
                options.SetRevocationEndpointUris("/oauth2/revoke");
                options.SetDeviceAuthorizationEndpointUris("/oauth2/device_authorization");
                options.SetEndUserVerificationEndpointUris("/oauth2/device");
                options.AllowAuthorizationCodeFlow().RequireProofKeyForCodeExchange();
                options.AllowClientCredentialsFlow().AllowRefreshTokenFlow().AllowDeviceAuthorizationFlow().AllowTokenExchangeFlow();
                options.RegisterScopes(Scopes.Profile, Scopes.Email, "sample.read", "sample.write");
                options.DisableAccessTokenEncryption(); // Native JwtBearer validates the signed access token.
                if (credentials is null)
                {
                    options.AddDevelopmentEncryptionCertificate().AddDevelopmentSigningCertificate();
                }
                else
                {
                    credentials(options);
                }
                var web = options.UseAspNetCore()
                    .EnableAuthorizationEndpointPassthrough()
                    .EnableEndUserVerificationEndpointPassthrough()
                    .EnableUserInfoEndpointPassthrough();
                if (development)
                {
                    web.DisableTransportSecurityRequirement();
                }
                options.AddEventHandler<OpenIddictServerEvents.HandleTokenRequestContext>(handler =>
                    handler.UseInlineHandler(context =>
                    {
                        var request = context.Request;
                        ClaimsPrincipal principal;
                        if (request.IsClientCredentialsGrantType())
                        {
                            principal = CreatePrincipal(request.ClientId!, request.GetScopes());
                            principal.SetClaim(Claims.ClientId, request.ClientId);
                        }
                        else if (request.IsTokenExchangeGrantType())
                        {
                            var subject = context.SubjectTokenPrincipal!;
                            var actor = context.ActorTokenPrincipal;
                            if (actor is null || actor.GetClaim(Claims.Subject) != request.ClientId
                                || actor.GetClaim(Claims.ClientId) != request.ClientId
                                || request.GetScopes().Any(scope => !subject.HasScope(scope) || !actor.HasScope(scope)))
                            {
                                context.Reject(Errors.InvalidGrant, "The actor and requested scopes must match the authenticated client and subject.");
                                return default;
                            }
                            principal = CreatePrincipal(subject.GetClaim(Claims.Subject)!, request.GetScopes());
                            principal.SetClaim(System.Security.Claims.ClaimTypes.TenantId, subject.GetClaim(System.Security.Claims.ClaimTypes.TenantId));
                            principal.SetClaim(Claims.Name, subject.GetClaim(Claims.Name));
                            principal.SetClaim(Claims.Email, subject.GetClaim(Claims.Email));
                            principal.SetClaim(Claims.ClientId, request.ClientId);
                            principal.SetClaim(Claims.Actor, JsonSerializer.SerializeToElement(new Dictionary<string, string>
                            {
                                [Claims.Subject] = actor.GetClaim(Claims.Subject)!
                            }));
                        }
                        else
                        {
                            principal = (context.AuthorizationCodePrincipal ?? context.RefreshTokenPrincipal ?? context.DeviceCodePrincipal)!
                                .Clone();
                            if (request.IsRefreshTokenGrantType() && request.GetScopes().Length > 0)
                            {
                                principal.SetScopes(request.GetScopes());
                            }
                        }
                        principal.SetDestinations(claim => claim.Type switch
                        {
                            Claims.Name when principal.HasScope(Scopes.Profile) => [Destinations.AccessToken, Destinations.IdentityToken],
                            Claims.Email when principal.HasScope(Scopes.Email) => [Destinations.AccessToken, Destinations.IdentityToken],
                            Claims.Name or Claims.Email => [],
                            _ => [Destinations.AccessToken]
                        });
                        context.Principal = principal;
                        return default;
                    }));
            });
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
        {
            options.Authority = issuer.AbsoluteUri.TrimEnd('/');
            options.Audience = "nof-sample";
            options.MapInboundClaims = false;
            options.RequireHttpsMetadata = !development;
        });
        services.AddAuthorization();
        if (initializeClients)
        {
            services.AddHostedService<SampleIdentityBootstrap>();
        }
        return services;
    }

    public static ClaimsPrincipal CreatePrincipal(string subject, IEnumerable<string> scopes)
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity("SampleIdentity", Claims.Name, Claims.Role));
        principal.SetClaim(Claims.Subject, subject);
        principal.SetClaim(System.Security.Claims.ClaimTypes.TenantId, "sample-tenant");
        if (subject == "demo-user")
        {
            principal.SetClaim(Claims.Name, "NOF Sample User");
            principal.SetClaim(Claims.Email, "sample.user@nof.local");
        }
        principal.SetScopes(scopes);
        principal.SetResources("nof-sample");
        return principal;
    }
}
