using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using NOF.Hosting;
using NOF.Hosting.AspNetCore;
using NOF.Sample.Application;
using NOF.Sample.Endpoints;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using Xunit;

namespace NOF.Sample.Tests;

public sealed class PermissionDemoTests
{
    [Theory]
    [InlineData(null, HttpStatusCode.Unauthorized)]
    [InlineData("unrelated", HttpStatusCode.Forbidden)]
    [InlineData(SamplePermissions.ViewDirectory, HttpStatusCode.OK)]
    [InlineData(SamplePermissions.ManageDirectory, HttpStatusCode.OK)]
    public async Task RealEndpoint_UsesContractAnyGroup_AfterNativeJwtValidation(string? permission, HttpStatusCode status)
    {
        using var rsa = RSA.Create(2048);
        var key = new RsaSecurityKey(rsa);
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Production" }).AddNOF();
        builder.WebHost.UseTestServer();
        builder.AddApplicationPart(typeof(PermissionDemoService).Assembly);
        builder.AddApplicationService<PermissionDemoService>();
        builder.Services.AddPermissions(SamplePermissions.Definitions);
        builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
        {
            options.MapInboundClaims = false;
            options.TokenValidationParameters = new()
            {
                ValidateIssuer = true,
                ValidIssuer = "https://permissions.local",
                ValidateAudience = true,
                ValidAudience = "permissions",
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = key
            };
        });
        await using var app = builder.Build();
        app.UseAuthentication();
        app.UseNOF();
        app.MapPermissionDemoEndpoints();
        await app.StartAsync();
        using var client = app.GetTestClient();
        if (permission is not null)
        {
            var token = new JwtSecurityToken("https://permissions.local", "permissions",
                [new("sub", "user-1"), new(ClaimTypes.Permission, permission)], expires: DateTime.UtcNow.AddMinutes(5),
                signingCredentials: new(key, SecurityAlgorithms.RsaSha256));
            client.DefaultRequestHeaders.Authorization = new("Bearer", new JwtSecurityTokenHandler().WriteToken(token));
        }
        using var response = await client.GetAsync("/api/permissions/overview");
        Assert.Equal(status, response.StatusCode);
        if (status == HttpStatusCode.OK)
        {
            var overview = await response.Content.ReadFromJsonAsync<PermissionDemoOverview>();
            Assert.Equal("user-1", overview!.UserId);
            Assert.Equal(2, overview.Permissions.Count);
        }
        else
        {
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        }
    }
}
