using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using NOF.Abstraction;
using NOF.Hosting;
using NOF.Hosting.AspNetCore;
using NOF.Infrastructure;
using NOF.Infrastructure.RabbitMQ;
using NOF.Sample.Endpoints;
using NOF.Sample.Endpoints.Models;
using NOF.Sample.Identity;
using NOF.Sample.Services;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Xunit;

namespace NOF.Sample.Tests;

public sealed class SampleOpenApiTests
{
    [Fact]
    public async Task ConfigurationSchema_MatchesNullableInput_BindingFailures_AndActualStructuredProblem()
    {
        await using var app = await ConfigNodeRestTests.CreateAppAsync();
        using var client = app.GetTestClient();
        using var spec = JsonDocument.Parse(await client.GetStringAsync("/openapi/v1.json"));
        var paths = spec.RootElement.GetProperty("paths");
        var permission = paths.GetProperty("/api/permissions/overview").GetProperty("get");
        Assert.Equal(2, permission.GetProperty("security").GetArrayLength());
        Assert.Contains("sample.permissions.read", permission.GetProperty("description").GetString());
        Assert.True(permission.GetProperty("responses").TryGetProperty("401", out _));
        Assert.True(permission.GetProperty("responses").TryGetProperty("403", out _));
        var create = paths.GetProperty("/api/config-nodes").GetProperty("post");
        var properties = create.GetProperty("requestBody").GetProperty("content").GetProperty("application/json").GetProperty("schema").GetProperty("properties");
        Assert.Contains("null", properties.GetProperty("parentId").GetRawText());
        var active = paths.GetProperty("/api/config-nodes/{id}/active-file").GetProperty("put");
        Assert.Contains("null", active.GetProperty("requestBody").GetRawText());
        Assert.Contains("ParentNotFound", create.GetProperty("responses").GetProperty("422").GetProperty("description").GetString());
        var badRequest = create.GetProperty("responses").GetProperty("400");
        Assert.Contains("InvalidInput", badRequest.GetProperty("description").GetString());
        Assert.Contains("Invalid request", badRequest.GetProperty("description").GetString());
        Assert.False(badRequest.GetProperty("content").GetProperty("application/problem+json").GetProperty("schema").TryGetProperty("anyOf", out _));
        Assert.True(create.GetProperty("responses").GetProperty("201").GetProperty("headers").TryGetProperty("Location", out _));
        foreach (var operation in new[] { create, paths.GetProperty("/api/config-nodes/{id}/parent").GetProperty("patch") })
        {
            Assert.True(operation.GetProperty("responses").TryGetProperty("400", out _));
            Assert.True(operation.GetProperty("responses").TryGetProperty("415", out _));
            Assert.False(string.IsNullOrEmpty(operation.GetProperty("operationId").GetString()));
            Assert.False(string.IsNullOrEmpty(operation.GetProperty("summary").GetString()));
        }
        using var created = await client.PostAsJsonAsync("/api/config-nodes", new { name = "nullable-root", parentId = (long?)null });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var node = (await created.Content.ReadFromJsonAsync<CreatedConfigNode>())!;
        using var clear = await client.PutAsJsonAsync($"/api/config-nodes/{node.Id}/active-file", new { fileName = (string?)null });
        Assert.Equal(HttpStatusCode.NoContent, clear.StatusCode);
        using var malformed = await client.PatchAsync($"/api/config-nodes/{node.Id}/parent", new StringContent("{", Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, malformed.StatusCode);
        using var media = await client.PatchAsync($"/api/config-nodes/{node.Id}/parent", new StringContent("{}", Encoding.UTF8, "text/plain"));
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, media.StatusCode);
        var stream = paths.GetProperty("/api/sample-events").GetProperty("get");
        var sse = stream.GetProperty("responses").GetProperty("200").GetProperty("content").GetProperty("text/event-stream");
        Assert.False(sse.TryGetProperty("schema", out _));
        Assert.Equal("application/json", sse.GetProperty("itemSchema").GetProperty("properties").GetProperty("data").GetProperty("contentMediaType").GetString());
        Assert.Contains(stream.GetProperty("parameters").EnumerateArray(), input => input.GetProperty("name").GetString() == "X-Tenant-Id");
        Assert.Contains("clamped", stream.GetProperty("description").GetString());
    }

    [Fact]
    public async Task NativeHostEndpoints_DocumentSecurityKnownErrorsAndResponses_AndEnforceAntiforgery()
    {
        await using var app = CreateHost();
        await app.StartAsync();
        using var client = app.GetTestClient();
        using var spec = JsonDocument.Parse(await client.GetStringAsync("/openapi/v1.json"));
        var schemes = spec.RootElement.GetProperty("components").GetProperty("securitySchemes");
        Assert.Equal("cookie", schemes.GetProperty("Session").GetProperty("in").GetString());
        Assert.Equal("bearer", schemes.GetProperty("Bearer").GetProperty("scheme").GetString());
        var paths = spec.RootElement.GetProperty("paths");
        foreach (var path in paths.EnumerateObject())
        {
            foreach (var operation in path.Value.EnumerateObject().Where(item => item.Name is "get" or "post"))
            {
                Assert.True(operation.Value.TryGetProperty("operationId", out _), path.Name);
                Assert.True(operation.Value.TryGetProperty("summary", out _), path.Name);
                foreach (var response in operation.Value.GetProperty("responses").EnumerateObject().Where(item => item.Name == "200"))
                {
                    Assert.True(response.Value.TryGetProperty("content", out _), path.Name);
                }
            }
        }
        var write = paths.GetProperty("/api/demo/cache/{action}").GetProperty("post");
        Assert.True(write.GetProperty("security")[0].TryGetProperty("CsrfHeader", out _));
        Assert.True(write.GetProperty("responses").TryGetProperty("415", out _));
        Assert.Contains("delete", write.GetProperty("parameters").GetRawText());
        using var missing = await client.PostAsJsonAsync("/api/demo/delivery/pause", new { });
        Assert.True(missing.StatusCode == HttpStatusCode.BadRequest, await missing.Content.ReadAsStringAsync());
        using var tokenResponse = await client.GetAsync("/api/demo/security-token");
        var token = (await tokenResponse.Content.ReadFromJsonAsync<SecurityTokenResponse>())!;
        client.DefaultRequestHeaders.Add("Cookie", tokenResponse.Headers.GetValues("Set-Cookie").Select(value => value.Split(';')[0]));
        client.DefaultRequestHeaders.Add(ExternalIdentity.AntiforgeryHeaderName, token.Token);
        using var invalid = await client.PostAsJsonAsync("/api/demo/delivery/unknown", new { });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        using var badBody = await client.PostAsync("/api/demo/cache/read", new StringContent("{", Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, badBody.StatusCode);
        using var wrongMedia = await client.PostAsync("/api/demo/cache/read", new StringContent("{}", Encoding.UTF8, "text/plain"));
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, wrongMedia.StatusCode);
        using var noOrder = await client.PostAsJsonAsync("/api/demo/redeliver", new { });
        Assert.Equal(HttpStatusCode.BadRequest, noOrder.StatusCode);
        using var paused = await client.PostAsJsonAsync("/api/demo/delivery/pause", new { });
        Assert.Equal(HttpStatusCode.OK, paused.StatusCode);
        Assert.True((await paused.Content.ReadFromJsonAsync<DemoDeliveryResponse>())!.Paused);
        using var value = await client.PostAsJsonAsync("/api/demo/value-object", new { value = "" });
        Assert.Equal(HttpStatusCode.BadRequest, value.StatusCode);
        Assert.False((await value.Content.ReadFromJsonAsync<DemoValueResponse>())!.Valid);
        using var downstream = await client.GetAsync("/api/demo/receipts");
        Assert.Equal(HttpStatusCode.BadGateway, downstream.StatusCode);
        using var login = await client.GetAsync("/auth/login?returnUrl=/demo.html");
        Assert.Equal(HttpStatusCode.Redirect, login.StatusCode);
        Assert.StartsWith("https://identity.example/authorize", login.Headers.Location!.ToString());
    }

    private static WebApplication CreateHost()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Development" }).AddNOF();
        builder.WebHost.UseTestServer();
        builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.RespectNullableAnnotations = true);
        builder.Services.Configure<RouteHandlerOptions>(options => options.ThrowOnBadRequest = false);
        builder.Services.AddAntiforgery(options => { options.HeaderName = ExternalIdentity.AntiforgeryHeaderName; options.Cookie.Name = ExternalIdentity.AntiforgeryCookieName; });
        builder.Services.AddSampleIdentity(builder.Configuration, builder.Environment);
        builder.Services.Configure<OpenIdConnectOptions>("OpenIdConnect", options => options.Configuration = new OpenIdConnectConfiguration
        {
            AuthorizationEndpoint = "https://identity.example/authorize",
            TokenEndpoint = "https://identity.example/token",
            Issuer = "https://identity.example"
        });
        builder.Services.AddSingleton<DemoDeliveryControl>();
        builder.Services.AddSingleton<DemoObservations>();
        builder.Services.AddScoped(_ => new ConfigurationDbContext(new DbContextOptionsBuilder<ConfigurationDbContext>().UseSqlite("Data Source=:memory:").Options));
        builder.Services.AddCacheService().AddMemoryCacheService();
        builder.Services.AddObjectStorage().AddMemoryObjectStorage();
        builder.Services.AddIdGenerator().AddSnowflakeIdGenerator();
        builder.Services.AddRabbitMQ(options => options.Uri = new Uri("amqp://guest:guest@localhost/"));
        builder.Services.AddHttpClient("notifications", client => client.BaseAddress = new Uri("https://notifications.example")).ConfigurePrimaryHttpMessageHandler(() => new UnavailableNotifications());
        builder.Services.AddOpenApi(options =>
        {
            options.AddDocumentTransformer<OpenApi.SampleOpenApiDocumentTransformer>();
            options.AddOperationTransformer<OpenApi.SampleOpenApiOperationTransformer>();
        });
        var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseNOF();
        app.UseStatusCodePages(async status => await Results.Problem(statusCode: status.HttpContext.Response.StatusCode).ExecuteAsync(status.HttpContext));
        app.UseAntiforgery();
        app.MapDemoCapabilities();
        app.MapSampleIdentity();
        app.MapOpenApi();
        return app;
    }

    public sealed class UnavailableNotifications : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
    }
}
