using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using NOF.Abstraction;
using NOF.Application;
using NOF.Contract;
using NOF.Hosting;
using NOF.Infrastructure;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Xunit;

namespace NOF.Hosting.AspNetCore.Tests;

public sealed class HttpBindingTests
{
    private static readonly ServiceOperationDescriptor<UpdateInput, UpdateReply> Update = new(typeof(IService), nameof(IService.Update));
    private static readonly ServiceOperationDescriptor<SearchInput, UpdateReply> Search = new(typeof(IService), nameof(IService.Search));
    private static readonly ServiceOperationDescriptor<UpdateInput, UpdateReply> Denied = new(typeof(IService), nameof(IService.Denied));
    [Fact]
    public async Task ConventionBinding_ProtectsRouteIdentity_AndNativeProjectionMatchesOpenApi()
    {
        await using var app = CreateApp();
        app.MapPut("/items/{id:long}", Update).Response((UpdateReply value) => TypedResults.Ok(value.Name));
        app.MapOpenApi();
        await app.StartAsync();
        using var client = app.GetTestClient();
        using var response = await client.PutAsJsonAsync("/items/42", new { id = 999, name = "stored" });
        Assert.Equal("stored", await response.Content.ReadFromJsonAsync<string>());
        using var document = JsonDocument.Parse(await client.GetStringAsync("/openapi/v1.json"));
        var operation = document.RootElement.GetProperty("paths").GetProperty("/items/{id}").GetProperty("put");
        var body = operation.GetProperty("requestBody").GetProperty("content").GetProperty("application/json").GetProperty("schema");
        Assert.True(body.GetProperty("properties").TryGetProperty("name", out _));
        Assert.False(body.GetProperty("properties").TryGetProperty("id", out _));
        Assert.Equal("string", operation.GetProperty("responses").GetProperty("200").GetProperty("content").GetProperty("application/json").GetProperty("schema").GetProperty("type").GetString());
    }

    [Fact]
    public async Task QueryMembers_MergeRepeatedConfigurationAndUseExplicitDefaults()
    {
        await using var app = CreateApp();
        var endpoint = app.MapGet("/search", Search).Request(input => input.Member(value => value.Page).FromQuery("old", 2));
        endpoint.Request(input => input.Member(value => value.Page).FromQuery("limit", 5));
        await app.StartAsync();
        using var client = app.GetTestClient();
        var result = await client.GetFromJsonAsync<UpdateReply>("/search?name=typed&tags=a&tags=b");
        Assert.Equal("typed:5:a,b", result!.Name);
    }

    [Fact]
    public async Task Response_ClosestGroupWins_AndEndpointReplacesTheEntireMapping()
    {
        await using var app = CreateApp();
        var root = app.MapGroup("/api");
        var inner = root.MapGroup("/items");
        inner.Response((UpdateReply value) => TypedResults.Created("/group-location", value.Name));
        // A late root declaration cannot override a more specific native group.
        root.Response<UpdateReply>(202);
        inner.MapPut("/{id:long}", Update);
        inner.MapPut("/plain/{id:long}", Update).Response<UpdateReply>(202);
        await app.StartAsync();
        using var client = app.GetTestClient();
        using var inherited = await client.PutAsJsonAsync("/api/items/1", new { name = "a" });
        Assert.Equal(HttpStatusCode.Created, inherited.StatusCode);
        Assert.Equal("a", await inherited.Content.ReadFromJsonAsync<string>());
        Assert.NotNull(inherited.Headers.Location);
        using var replaced = await client.PutAsJsonAsync("/api/items/plain/2", new { name = "b" });
        Assert.Equal(HttpStatusCode.Accepted, replaced.StatusCode);
        Assert.Null(replaced.Headers.Location);
        Assert.Equal(2, (await replaced.Content.ReadFromJsonAsync<UpdateReply>())!.Id);
    }

    [Fact]
    public async Task RepeatedResponses_LastMappingWinsWithoutExecutingProvidersForDocumentation()
    {
        await using var app = CreateApp();
        var calls = 0;
        app.MapPut("/items/{id:long}", Update).Response<UpdateReply>(201).Response((UpdateReply value) =>
        {
            calls++;
            return TypedResults.Ok(value.Name);
        });
        app.MapOpenApi();
        await app.StartAsync();
        using var client = app.GetTestClient();
        using var document = JsonDocument.Parse(await client.GetStringAsync("/openapi/v1.json"));
        var responses = document.RootElement.GetProperty("paths").GetProperty("/items/{id}").GetProperty("put").GetProperty("responses");
        Assert.True(responses.TryGetProperty("200", out _));
        Assert.False(responses.TryGetProperty("201", out _));
        Assert.Equal(0, calls);
        using var response = await client.PutAsJsonAsync("/items/1", new { name = "a" });
        Assert.Equal("a", await response.Content.ReadFromJsonAsync<string>());
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task FilterFailures_UseConcreteTypes_AndExcludedCasesDoNotAppearInOpenApi()
    {
        foreach (var exclude in new[]
        {
            false,
            true
        }

        )
        {
            await using var app = CreateApp(deny: true, exclude: exclude);
            app.MapGroup("/api").Response<RateFailure>(429).MapPut("/{id:long}", Denied);
            app.MapOpenApi();
            await app.StartAsync();
            using var client = app.GetTestClient();
            using var response = await client.PutAsJsonAsync("/api/1", new { name = "a" });
            Assert.Equal(exclude ? 200 : 429, (int)response.StatusCode);
            if (!exclude)
            {
                using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                Assert.Equal("000429", problem.RootElement.GetProperty("title").GetString());
                Assert.False(problem.RootElement.TryGetProperty("code", out _));
                Assert.Equal("about:blank", problem.RootElement.GetProperty("type").GetString());
            }

            using var document = JsonDocument.Parse(await client.GetStringAsync("/openapi/v1.json"));
            var responses = document.RootElement.GetProperty("paths").GetProperty("/api/{id}").GetProperty("put").GetProperty("responses");
            Assert.Equal(!exclude, responses.TryGetProperty("429", out _));
        }
    }

    [Fact]
    public async Task Context_IsExplicit_AndNativeResponseDoesNotReadBusinessContextBack()
    {
        await using var app = CreateApp(normalize: true);
        app.MapPut("/items/{id:long}", Update).Context(context => context.TenantFromHeader("X-Tenant").Item("correlation").FromHeader("X-Correlation")).Response((UpdateReply value, HttpContext http) =>
        {
            http.Response.Headers["X-Boundary-Tenant"] = http.Request.Headers["X-Tenant"];
            return TypedResults.Ok(value);
        });
        await app.StartAsync();
        using var client = app.GetTestClient();
        client.DefaultRequestHeaders.Add("X-Tenant", "boundary");
        client.DefaultRequestHeaders.Add("X-Correlation", "request-id");
        client.DefaultRequestHeaders.Add("X-Unlisted", "must-not-flow");
        using var response = await client.PutAsJsonAsync("/items/1", new { name = "a" });
        var result = await response.Content.ReadFromJsonAsync<UpdateReply>();
        Assert.Equal("business", result!.Tenant);
        Assert.Equal("request-id", result.Correlation);
        Assert.False(result.Unlisted);
        Assert.Equal("boundary", response.Headers.GetValues("X-Boundary-Tenant").Single());
        Assert.Equal("host", Context.Current.TenantId);
    }

    [Theory]
    [InlineData("{}", "application/json", 400)]
    [InlineData("{", "application/json", 400)]
    [InlineData("{\"name\":\"a\"}", "text/plain", 415)]
    public async Task BadInputs_DoNotConstructBusinessHandlers(string body, string media, int expected)
    {
        await using var app = CreateApp();
        app.MapPut("/items/{id:long}", Update);
        await app.StartAsync();
        using var client = app.GetTestClient();
        using var response = await client.PutAsync("/items/1", new StringContent(body, Encoding.UTF8, media));
        Assert.Equal(expected, (int)response.StatusCode);
        Assert.Equal(0, app.Services.GetRequiredService<Probe>().Constructed);
    }

    [Fact]
    public async Task FullMap_IsNotRunAtStartupOrForOpenApi_AndCombinesRequiredInitAndRoute()
    {
        await using var app = CreateApp();
        var calls = 0;
        app.MapPut("/items/{key:long}", Update).Request(request => request.Route<long>("key").Body<UpdateBody>().Map((key, body) =>
        {
            calls++;
            return new UpdateInput(key, body.Name);
        }));
        app.MapOpenApi();
        await app.StartAsync();
        using var client = app.GetTestClient();
        _ = await client.GetStringAsync("/openapi/v1.json");
        Assert.Equal(0, calls);
        using var response = await client.PutAsJsonAsync("/items/15", new { name = "map" });
        Assert.Equal(15, (await response.Content.ReadFromJsonAsync<UpdateReply>())!.Id);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task WholeBody_CollectionsAreReadOnce_AndMultipleBodiesAreRejected()
    {
        await using var app = CreateApp();
        app.MapPut("/collection", Update).Request(request => request.Body<string[]>().Map(values => new UpdateInput(7, string.Join(',', values))));
        var invalid = new HttpRequestBuilder<UpdateInput>();
        Assert.Throws<InvalidOperationException>(() => invalid.Body<string[]>().Body<string>());
        await app.StartAsync();
        using var client = app.GetTestClient();
        using var response = await client.PutAsJsonAsync("/collection", new[] { "a", "b" });
        Assert.Equal("a,b", (await response.Content.ReadFromJsonAsync<UpdateReply>())!.Name);
    }

    [Fact]
    public async Task ContextOverrides_PruneUnusedInputs_AndDefaultsKeepJsonTypesInOpenApi()
    {
        await using var app = CreateApp();
        var group = app.MapGroup("/api").Context(context => context.Item("correlation").FromHeader("X-Old"));
        group.MapGet("/search", Search).Context(context => context.Item("correlation").FromValue("constant")).Request(request => request.Member(value => value.Page).FromQuery("limit", 5));
        app.MapOpenApi();
        await app.StartAsync();
        using var client = app.GetTestClient();
        using var document = JsonDocument.Parse(await client.GetStringAsync("/openapi/v1.json"));
        var parameters = document.RootElement.GetProperty("paths").GetProperty("/api/search").GetProperty("get").GetProperty("parameters").EnumerateArray().ToArray();
        Assert.DoesNotContain(parameters, parameter => parameter.GetProperty("name").GetString() == "X-Old");
        var limit = parameters.Single(parameter => parameter.GetProperty("name").GetString() == "limit");
        Assert.Equal(5, limit.GetProperty("schema").GetProperty("default").GetInt32());
    }

    [Fact]
    public async Task CustomInput_DeclaresItsMediaType_AndUserHookExceptionsPropagate()
    {
        await using var app = CreateApp();
        app.MapPut("/text", Update).Request(request => request.Custom(async (http, ct) =>
        {
            using var reader = new StreamReader(http.Request.Body);
            return new UpdateInput(1, await reader.ReadToEndAsync(ct));
        }, new(typeof(string), "text/plain")));
        app.MapPut("/broken", Update).Request(request => request.Body<UpdateBody>().Map(_ => throw new ArgumentException("hook failed")));
        app.MapOpenApi();
        await app.StartAsync();
        using var client = app.GetTestClient();
        using var response = await client.PutAsync("/text", new StringContent("plain"));
        Assert.Equal("plain", (await response.Content.ReadFromJsonAsync<UpdateReply>())!.Name);
        using var document = JsonDocument.Parse(await client.GetStringAsync("/openapi/v1.json"));
        var media = document.RootElement.GetProperty("paths").GetProperty("/text").GetProperty("put").GetProperty("requestBody").GetProperty("content");
        Assert.True(media.TryGetProperty("text/plain", out _));
        await Assert.ThrowsAsync<ArgumentException>(() => client.PutAsJsonAsync("/broken", new { name = "a" }));
    }

    [Fact]
    public async Task CompleteProblemResult_UsesExplicitNativeProducesMetadataWithoutRunningFactory()
    {
        await using var app = CreateApp(deny: true);
        var calls = 0;
        app.MapPut("/items/{id:long}", Denied).Response((RateFailure failure) =>
        {
            calls++;
            return TypedResults.Problem(statusCode: 429, type: "about:blank", title: failure.Title, detail: failure.Detail);
        }).NativeBuilder.ProducesProblem(429);
        app.MapOpenApi();
        await app.StartAsync();
        using var client = app.GetTestClient();
        using var document = JsonDocument.Parse(await client.GetStringAsync("/openapi/v1.json"));
        var responses = document.RootElement.GetProperty("paths").GetProperty("/items/{id}").GetProperty("put").GetProperty("responses");
        Assert.True(responses.TryGetProperty("429", out _));
        Assert.Equal(0, calls);
        using var response = await client.PutAsJsonAsync("/items/1", new { name = "a" });
        Assert.Equal(429, (int)response.StatusCode);
        Assert.Equal(1, calls);
    }

    private static WebApplication CreateApp(bool deny = false, bool exclude = false, bool normalize = false)
    {
        var builder = WebApplication.CreateBuilder([]).AddNOF();
        builder.WebHost.UseTestServer();
        builder.AddApplicationService<Server>();
        builder.Services.AddSingleton<Probe>();
        builder.Services.AddScoped<UpdateHandler>();
        builder.Services.AddScoped<SearchHandler>();
        if (deny)
        {
            builder.Services.AddInvocationFilter<RateFilter>();
        }

        if (exclude)
        {
            builder.Services.AddInvocationFilters(filters => filters.UseCase(Denied).Exclude<RateFilter>());
        }

        if (normalize)
        {
            builder.Services.AddInvocationFilter<Normalize>();
        }

        return builder.Build();
    }

    public interface IService : IApplicationService
    {
        UpdateReply Update(UpdateInput input);
        UpdateReply Search(SearchInput input);
        UpdateReply Denied(UpdateInput input);
    }

    public sealed record UpdateInput(long Id, string Name);
    public sealed record SearchInput(string Name, int Page = 1, List<string>? Tags = null);
    public sealed record UpdateReply(long Id, string Name, string Tenant, string? Correlation, bool Unlisted);
    public sealed record UpdateBody
    {
        public required string Name { get; init; }
    }

    public sealed class Probe
    {
        public int Constructed { get; set; }
    }

    public sealed class Server : ApplicationService<IService>, IApplicationServiceImplementation
    {
        public static IReadOnlyDictionary<string, ServiceOperationMapping> HandlerMappings { get; } = new Dictionary<string, ServiceOperationMapping>
        {
            [nameof(IService.Update)] = new(typeof(UpdateHandler), typeof(UpdateInput), typeof(UpdateReply))
            {
                Descriptor = Update
            },
            [nameof(IService.Search)] = new(typeof(SearchHandler), typeof(SearchInput), typeof(UpdateReply))
            {
                Descriptor = Search
            },
            [nameof(IService.Denied)] = new(typeof(UpdateHandler), typeof(UpdateInput), typeof(UpdateReply))
            {
                Descriptor = Denied
            }
        };

    }

    public sealed class UpdateHandler : ServiceOperation<UpdateInput, UpdateReply>
    {
        public UpdateHandler(Probe probe) => probe.Constructed++;
        public override Task<UpdateReply> HandleAsync(UpdateInput input, InvocationContext context, CancellationToken ct) => Task.FromResult(new UpdateReply(input.Id, input.Name, context.TenantId, context.Context["correlation"]?.ToString(), context.TryGetItem("X-Unlisted", out _)));
    }

    public sealed class SearchHandler : ServiceOperation<SearchInput, UpdateReply>
    {
        public override Task<UpdateReply> HandleAsync(SearchInput input, InvocationContext context, CancellationToken ct) => Task.FromResult(new UpdateReply(0, $"{input.Name}:{input.Page}:{string.Join(',', input.Tags ?? [])}", context.TenantId, null, false));
    }

    public sealed record RateFailure(string Detail) : IFailure
    {
        public string Title => "000429";
    }

    public sealed class RateFilter : IInvocationFilter
    {
        public static IReadOnlyList<InvocationFailureCase> FailureCases => [new("rate", typeof(RateFailure), typeof(RateFailure), "000429", value => value, value => ((RateFailure)value).Detail)];

        public ValueTask<object?> InvokeAsync(InvocationContext context, object input, InvocationDelegate next, CancellationToken ct) => ValueTask.FromResult<object?>(new RateFailure("Wait before retrying"));
    }

    public sealed class Normalize : IInvocationFilter
    {
        public ValueTask<object?> InvokeAsync(InvocationContext context, object input, InvocationDelegate next, CancellationToken ct) => next(context.WithTenantId("business"), input, ct);
    }
}
