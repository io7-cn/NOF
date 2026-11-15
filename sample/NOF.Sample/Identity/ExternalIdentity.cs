using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using NOF.Abstraction;
using System.Security.Claims;

namespace NOF.Sample.Identity;

/// <summary>Consumes an external identity provider; this application implements no identity protocol endpoints.</summary>
public static class ExternalIdentity
{
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
                options.Cookie.Name = "nof.sample.session";
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
            new AuthenticationProperties { RedirectUri = LocalReturnUrl(returnUrl) }, [OpenIdConnectDefaults.AuthenticationScheme]));
        endpoints.MapPost("/auth/logout", () => Results.SignOut(new AuthenticationProperties { RedirectUri = "/demo.html" },
            [CookieAuthenticationDefaults.AuthenticationScheme, OpenIdConnectDefaults.AuthenticationScheme]))
            .AddEndpointFilter(async (context, next) =>
            {
                try
                {
                    await context.HttpContext.RequestServices.GetRequiredService<Microsoft.AspNetCore.Antiforgery.IAntiforgery>()
                        .ValidateRequestAsync(context.HttpContext);
                }
                catch (Microsoft.AspNetCore.Antiforgery.AntiforgeryValidationException)
                {
                    return Results.BadRequest("Refresh the page and try again.");
                }
                return await next(context);
            });
        endpoints.MapGet("/auth/status", (HttpContext http, IConfiguration configuration) => Results.Ok(new
        {
            authenticated = http.User.Identity?.IsAuthenticated == true,
            name = http.User.Identity?.Name,
            subject = http.User.FindFirst("sub")?.Value,
            permissions = http.User.FindAll(ClaimTypes.Permission).Select(claim => claim.Value).ToArray(),
            authority = configuration["Authentication:Authority"] ?? "http://localhost:18080/realms/nof"
        }));
    }

    public static string LocalReturnUrl(string? value)
        => value is not null && value.StartsWith('/') && !value.StartsWith("//", StringComparison.Ordinal)
            && !value.StartsWith("/\\", StringComparison.Ordinal) ? value : "/demo.html";
}
