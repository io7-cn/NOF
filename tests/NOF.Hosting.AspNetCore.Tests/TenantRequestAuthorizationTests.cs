using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using NOF.Abstraction;
using NOF.Application;
using NOF.Contract;
using NOF.Infrastructure;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Xunit;

namespace NOF.Hosting.AspNetCore.Tests;

public sealed class TenantRequestAuthorizationTests
{
    [Theory]
    [InlineData(null, "forged", false, "/tenant", false, 401, null)]
    [InlineData(null, "forged", true, "/tenant", false, 403, null)]
    [InlineData("host", "forged", true, "/tenant", false, 403, null)]
    [InlineData("tenanta", "tenantb", true, "/tenant", false, 403, null)]
    [InlineData("unknown", "unknown", true, "/tenant", false, 403, null)]
    [InlineData("tenanta", null, true, "/tenant", false, 200, "tenanta")]
    [InlineData("tenanta", "tenanta", true, "/tenant", false, 200, "tenanta")]
    [InlineData(null, "host", true, "/host", false, 403, null)]
    [InlineData(null, "host", true, "/host", true, 403, null)]
    [InlineData("tenanta", null, true, "/host", true, 200, "tenanta")]
    [InlineData(null, null, false, "/stream", false, 401, null)]
    [InlineData("tenanta", null, true, "/tenant", false, 401, null, "issuer")]
    [InlineData("tenanta", null, true, "/tenant", false, 401, null, "audience")]
    [InlineData("tenanta", null, true, "/tenant", false, 401, null, "expired")]
    [InlineData("tenanta", null, true, "/tenant", false, 401, null, "signature")]
    [InlineData("tenanta", null, true, "/tenant", false, 401, null, "malformed")]
    [InlineData("tenanta", null, false, "/tenant", false, 401, null, "injected")]
    public async Task HttpRequests_ShouldAuthorizeBeforeResolvingTenantServices(
        string? identityTenant, string? headerTenant, bool sendToken, string path,
        bool platformPermission, int expectedStatus, string? expectedTenant, string? tokenError = null)
    {
        using var rsa = RSA.Create(2048);
        var key = new RsaSecurityKey(rsa) { KeyId = "test-key" };
        var builder = WebApplication.CreateBuilder([]).AddNOF();
        builder.WebHost.UseTestServer();
        builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
        {
            options.MapInboundClaims = false;
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidIssuer = "https://identity.local",
                ValidAudience = "tenant-api",
                IssuerSigningKey = key,
                ValidateLifetime = true,
                ClockSkew = TimeSpan.Zero
            };
        });
        builder.Services.Configure<TenantAuthorizationOptions>(options =>
        {
            options.RequireAuthenticatedTenant = true;
            options.RejectMismatchedTenant = true;
        });
        builder.Services.AddScoped<DefaultInboundAuthorizationHandler>();
        builder.Services.AddScoped<IInboundAuthorizationHandler, CatalogAuthorizationHandler>();
        var resolvedTenants = new List<string>();
        builder.Services.AddTransient(_ =>
        {
            // Represents tenant-specific service/connection resolution, before HandleAsync.
            resolvedTenants.Add(Context.Current.TenantId);
            return new TenantHandler();
        });
        builder.Services.AddTransient<StreamHandler>();
        builder.AddApplicationService<TenantServer>();
        await using var app = builder.Build();
        if (tokenError == "injected")
        {
            app.Use(async (http, next) =>
            {
                http.RequestServices.GetRequiredService<IUserContext>().User.AddIdentity(new ClaimsIdentity(
                    [new Claim(ClaimTypes.TenantId, "tenanta")], "untrusted-scoped-state"));
                await next(http);
            });
        }
        app.UseAuthentication();
        app.UseNOF();
        app.MapHealthChecks("/health");
        app.MapHealthChecks("/alive");
        var routes = app.MapGroup("").Context(context => context.TenantFromHeader(NOFAbstractionConstants.Transport.Headers.TenantId));
        routes.MapPost("/tenant", new ServiceOperationDescriptor<Empty, Testing.TestOutcome<string>>(typeof(ITenantService), nameof(ITenantService.Tenant)));
        routes.MapPost("/host", new ServiceOperationDescriptor<Empty, Testing.TestOutcome<string>>(typeof(ITenantService), nameof(ITenantService.Host)));
        routes.MapPost("/stream", new ServiceOperationDescriptor<Empty, Testing.TestOutcome<IAsyncEnumerable<string>>>(typeof(ITenantService), nameof(ITenantService.Stream)));
        app.MapOpenApi();
        await app.StartAsync();
        using var client = app.GetTestClient();
        if (sendToken)
        {
            var claims = new List<Claim> { new(JwtRegisteredClaimNames.Sub, "user") };
            if (identityTenant is not null)
            {
                claims.Add(new Claim(ClaimTypes.TenantId, identityTenant));
            }
            if (platformPermission)
            {
                claims.Add(new Claim(ClaimTypes.Permission, "platform.manage"));
            }
            using var otherRsa = RSA.Create(2048);
            var token = new JwtSecurityToken(tokenError == "issuer" ? "https://forged.local" : "https://identity.local",
                tokenError == "audience" ? "another-api" : "tenant-api", claims,
                DateTime.UtcNow.AddMinutes(-10), DateTime.UtcNow.AddMinutes(tokenError == "expired" ? -5 : 5),
                new SigningCredentials(tokenError == "signature" ? new RsaSecurityKey(otherRsa) : key, SecurityAlgorithms.RsaSha256));
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokenError == "malformed" ? "untrusted" : new JwtSecurityTokenHandler().WriteToken(token));
        }
        if (headerTenant is not null)
        {
            client.DefaultRequestHeaders.Add(NOFAbstractionConstants.Transport.Headers.TenantId, headerTenant);
        }

        using var response = await client.PostAsJsonAsync(path, new Empty());
        Assert.Equal((HttpStatusCode)expectedStatus, response.StatusCode);
        if (expectedTenant is null)
        {
            Assert.Empty(resolvedTenants);
            Assert.NotNull(await response.Content.ReadFromJsonAsync<Microsoft.AspNetCore.Mvc.ProblemDetails>());
        }
        else
        {
            Assert.Equal(expectedTenant, Assert.Single(resolvedTenants));
            Assert.Equal(expectedTenant, await response.Content.ReadFromJsonAsync<string>());
        }

        var endpoint = ((IEndpointRouteBuilder)app).DataSources.SelectMany(source => source.Endpoints)
            .OfType<RouteEndpoint>().Single(endpoint => endpoint.RoutePattern.RawText == path);
        var responses = endpoint.Metadata.GetOrderedMetadata<IProducesResponseTypeMetadata>();
        Assert.Contains(responses, metadata => metadata.StatusCode == 401 && metadata.ContentTypes.Contains("application/problem+json"));
        Assert.Contains(responses, metadata => metadata.StatusCode == 403 && metadata.ContentTypes.Contains("application/problem+json"));

        using var openApi = JsonDocument.Parse(await client.GetStringAsync("/openapi/v1.json"));
        var documentedResponses = openApi.RootElement.GetProperty("paths").GetProperty(path)
            .GetProperty("post").GetProperty("responses");
        Assert.True(documentedResponses.GetProperty("401").GetProperty("content").TryGetProperty("application/problem+json", out _));
        Assert.True(documentedResponses.GetProperty("403").GetProperty("content").TryGetProperty("application/problem+json", out _));
    }

    private static IResult Present(object? result) => result is IResult native
        ? native : TypedResults.Ok(InvocationResult.Value(result));

    public interface ITenantService : IApplicationService
    {
        [RequirePermission]
        Testing.TestOutcome<string> Tenant(Empty request);

        [RequirePermission("platform.manage")]
        Testing.TestOutcome<string> Host(Empty request);

        [RequirePermission]
        Testing.TestOutcome<IAsyncEnumerable<string>> Stream(Empty request);
    }

    public sealed class TenantServer : ApplicationService<ITenantService>, IApplicationServiceImplementation
    {
        public static IReadOnlyDictionary<string, ServiceOperationMapping> HandlerMappings { get; } =
            new Dictionary<string, ServiceOperationMapping>
            {
                [nameof(ITenantService.Tenant)] = new(typeof(TenantHandler), typeof(Empty), typeof(Testing.TestOutcome<string>)),
                [nameof(ITenantService.Host)] = new(typeof(TenantHandler), typeof(Empty), typeof(Testing.TestOutcome<string>)),
                [nameof(ITenantService.Stream)] = new(typeof(StreamHandler), typeof(Empty), typeof(Testing.TestOutcome<IAsyncEnumerable<string>>))
            };

    }

    public sealed class TenantHandler : ServiceOperation<Empty, Testing.TestOutcome<string>>
    {
        public override Task<Testing.TestOutcome<string>> HandleAsync(Empty request, InvocationContext context, CancellationToken cancellationToken)
            => Task.FromResult(NOF.Testing.TestOutcome.Success(context.TenantId));
    }

    public sealed class StreamHandler : ServiceOperation<Empty, Testing.TestOutcome<IAsyncEnumerable<string>>>
    {
        public override Task<Testing.TestOutcome<IAsyncEnumerable<string>>> HandleAsync(Empty request, InvocationContext context, CancellationToken cancellationToken)
            => throw new InvalidOperationException("Unauthorized streaming handlers must not run");
    }

    public sealed class CatalogAuthorizationHandler(DefaultInboundAuthorizationHandler defaultHandler) : IInboundAuthorizationHandler
    {
        public async ValueTask<InboundAuthorizationResult> AuthorizeAsync(InboundAuthorizationContext context, CancellationToken cancellationToken)
        {
            var result = await defaultHandler.AuthorizeAsync(context, cancellationToken);
            if (result is InboundAuthorizationResult.Denied || !context.User.IsAuthenticated)
            {
                return result;
            }
            // Application-owned catalog/readiness check uses trusted identity, before service resolution.
            return context.User.TenantId == "tenanta"
                ? result
                : new InboundAuthorizationResult.Denied(NOF.Testing.TestOutcome.Fail("403", "Tenant is not available"), AuthorizationFailureReason.Authorization, []);
        }
    }

}
