using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.OpenApi;
using NOF.Abstraction;
using System.Security.Claims;

namespace NOF.Sample.Identity;

/// <summary>Consumes an external identity provider; this application implements no identity protocol endpoints.</summary>
public static class ExternalIdentity
{
    public const string SessionCookieName = "nof.sample.session";
    public const string AntiforgeryHeaderName = "X-CSRF-TOKEN";
    public const string AntiforgeryCookieName = "nof.sample.antiforgery";
    public static IServiceCollection AddSampleIdentity(this IServiceCollection services, IConfiguration configuration, IHostEnvironment environment)
    {
        var authority = configuration["Authentication:Authority"] ?? "http://localhost:18080/realms/nof";
        services.AddAuthentication(options =>
        {
            options.DefaultScheme = "sample";
            options.DefaultChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme;
        }).AddPolicyScheme("sample", "Cookie or bearer", options => options.ForwardDefaultSelector = http =>
            http.Request.Headers.Authorization.ToString().StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
                ? JwtBearerDefaults.AuthenticationScheme : CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(options =>
            {
                options.Cookie.Name = SessionCookieName;
                options.LoginPath = "/auth/login";
                options.Events.OnRedirectToLogin = context =>
                {
                    if (context.Request.Path.StartsWithSegments("/api"))
                    {
                        context.Response.StatusCode = 401;
                    }
                    else
                    {
                        context.Response.Redirect(context.RedirectUri);
                    }
                    return Task.CompletedTask;
                };
            })
            .AddOpenIdConnect(options =>
            {
                options.Authority = authority;
                options.ClientId = configuration["Authentication:ClientId"] ?? "nof-sample-web";
                options.ClientSecret = configuration["Authentication:ClientSecret"];
                options.ResponseType = OpenIdConnectResponseType.Code;
                options.ResponseMode = OpenIdConnectResponseMode.Query;
                options.UsePkce = true;
                options.SaveTokens = true;
                options.MapInboundClaims = false;
                options.RequireHttpsMetadata = !environment.IsDevelopment();
                options.TokenValidationParameters.NameClaimType = "preferred_username";
                options.Scope.Add("profile");
                options.Scope.Add("email");
                if (environment.IsDevelopment())
                {
                    options.CorrelationCookie.SameSite = SameSiteMode.Lax;
                    options.CorrelationCookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
                    options.NonceCookie.SameSite = SameSiteMode.Lax;
                    options.NonceCookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
                }
            })
            .AddJwtBearer(options =>
            {
                options.Authority = authority;
                options.Audience = "nof-sample";
                options.MapInboundClaims = false;
                options.RequireHttpsMetadata = !environment.IsDevelopment();
                options.TokenValidationParameters.NameClaimType = "preferred_username";
            });
        services.AddAuthorization();
        return services;
    }

    public static void MapSampleIdentity(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/auth/login", (string? returnUrl) => Results.Challenge(
            new AuthenticationProperties { RedirectUri = LocalReturnUrl(returnUrl) }, [OpenIdConnectDefaults.AuthenticationScheme]))
            .WithName("Login").WithTags("Identity").WithSummary("Start the external OIDC authorization-code login")
            .WithDescription("Browser navigation: redirects to the configured authority; the native OIDC middleware handles /signin-oidc. returnUrl must be local, otherwise /demo.html is used.")
            .Produces(302).AddOpenApiOperationTransformer(DescribeRedirectAsync);
        endpoints.MapPost("/auth/logout", () => Results.SignOut(new AuthenticationProperties { RedirectUri = "/demo.html" },
            [CookieAuthenticationDefaults.AuthenticationScheme, OpenIdConnectDefaults.AuthenticationScheme]))
            .WithName("Logout").WithTags("Identity").WithSummary("Sign out the local session and external OIDC session")
            .WithDescription("Requires the antiforgery token and cookie from /api/demo/security-token. Redirects through the provider and back to /demo.html.")
            .Produces(302).ProducesProblem(400).AddOpenApiOperationTransformer(DescribeRedirectAsync)
            .WithMetadata(new Microsoft.AspNetCore.Antiforgery.RequireAntiforgeryTokenAttribute())
            .AddEndpointFilter(async (context, next) =>
            {
                if (context.HttpContext.Features.Get<Microsoft.AspNetCore.Antiforgery.IAntiforgeryValidationFeature>() is not { IsValid: true })
                {
                    return Results.Problem("Refresh the page and try again.", statusCode: 400);
                }
                return await next(context);
            });
        endpoints.MapGet("/auth/status", (HttpContext http, IConfiguration configuration) => Results.Ok(new IdentityStatusResponse(
            http.User.Identity?.IsAuthenticated == true, http.User.Identity?.Name, http.User.FindFirst("sub")?.Value,
            http.User.FindAll(ClaimTypes.Permission).Select(claim => claim.Value).ToArray(),
            configuration["Authentication:Authority"] ?? "http://localhost:18080/realms/nof")))
            .WithName("GetIdentityStatus").WithTags("Identity").WithSummary("Inspect the current cookie or bearer identity")
            .Produces<IdentityStatusResponse>();
    }

    private static Task DescribeRedirectAsync(OpenApiOperation operation, OpenApiOperationTransformerContext context, CancellationToken ct)
    {
        var response = (OpenApiResponse)operation.Responses!["302"];
        response.Headers ??= new Dictionary<string, IOpenApiHeader>();
        response.Headers["Location"] = new OpenApiHeader { Description = "Browser redirect to the external identity provider or local return path.", Schema = new OpenApiSchema { Type = JsonSchemaType.String, Format = "uri-reference" } };
        return Task.CompletedTask;
    }

    public static string LocalReturnUrl(string? value)
        => value is not null && value.StartsWith('/') && !value.StartsWith("//", StringComparison.Ordinal)
            && !value.StartsWith("/\\", StringComparison.Ordinal) ? value : "/demo.html";
}
