using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace NOF.Sample.OpenApi;

/// <summary>Documents this host's concrete cookie, bearer and antiforgery schemes.</summary>
public sealed class SampleOpenApiDocumentTransformer : IOpenApiDocumentTransformer
{
    public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context, CancellationToken cancellationToken)
    {
        document.Components ??= new OpenApiComponents();
        document.Components.SecuritySchemes = new Dictionary<string, IOpenApiSecurityScheme>
        {
            ["Session"] = new OpenApiSecurityScheme { Type = SecuritySchemeType.ApiKey, In = ParameterLocation.Cookie, Name = NOF.Sample.Identity.ExternalIdentity.SessionCookieName, Description = "Browser session established by GET /auth/login through the external OIDC provider." },
            ["Bearer"] = new OpenApiSecurityScheme { Type = SecuritySchemeType.Http, Scheme = "bearer", BearerFormat = "JWT", Description = "Access token issued by the configured external authority for audience nof-sample." },
            ["CsrfHeader"] = new OpenApiSecurityScheme { Type = SecuritySchemeType.ApiKey, In = ParameterLocation.Header, Name = NOF.Sample.Identity.ExternalIdentity.AntiforgeryHeaderName, Description = "Request token from GET /api/demo/security-token. Send the accompanying antiforgery cookie too." },
            ["CsrfCookie"] = new OpenApiSecurityScheme { Type = SecuritySchemeType.ApiKey, In = ParameterLocation.Cookie, Name = NOF.Sample.Identity.ExternalIdentity.AntiforgeryCookieName, Description = "Cookie issued with the antiforgery request token; retain it in the same browser/client cookie jar." }
        };
        return Task.CompletedTask;
    }
}
