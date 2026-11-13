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
    public async Task ConventionBinding_ProtectsRouteIdentity_ProjectsBodyAndMatchesOpenApi()
    {
        await using var app = CreateApp();
        app.MapPut("/items/{id:long}", Update).Response<UpdateReply>(output => output.Body(value => value.Name).Header("X-Id", value => value.Id));
        app.MapOpenApi();
        await app.StartAsync();
        using var client = app.GetTestClient();
        using var response = await client.PutAsJsonAsync("/items/42", new { id = 999, name = "stored" });
        Assert.Equal("stored", await response.Content.ReadFromJsonAsync<string>());
        Assert.Equal("42", response.Headers.GetValues("X-Id").Single());
        using var document = JsonDocument.Parse(await client.GetStringAsync("/openapi/v1.json"));
        var operation = document.RootElement.GetProperty("paths").GetProperty("/items/{id}").GetProperty("put");
        var body = operation.GetProperty("requestBody").GetProperty("content").GetProperty("application/json").GetProperty("schema");
        Assert.True(body.GetProperty("properties").TryGetProperty("name", out _));
        Assert.False(body.GetProperty("properties").TryGetProperty("id", out _));
        Assert.Equal("string", operation.GetProperty("responses").GetProperty("200").GetProperty("content").GetProperty("application/json").GetProperty("schema").GetProperty("type").GetString());
        Assert.True(operation.GetProperty("responses").GetProperty("200").GetProperty("headers").TryGetProperty("X-Id", out _));
    }

    [Fact]
    public async Task QueryMembers_MergeRepeatedConfigurationAndUseExplicitDefaults()
    {
        await using var app = CreateApp();
        var group = app.MapGroup("/api");
        var endpoint = group.MapGet("/search", Search).Request(input => input.Member(value => value.Page).FromQuery("old", defaultValue: 2));
        endpoint.Request(input => input.Member(value => value.Page).FromQuery("limit", defaultValue: 5));
        await app.StartAsync();
        using var client = app.GetTestClient();
        var result = await client.GetFromJsonAsync<UpdateReply>("/api/search?name=typed&tags=a&tags=b");
        Assert.Equal("typed:5:a,b", result!.Name);
        var overrideValue = await client.GetFromJsonAsync<UpdateReply>("/api/search?name=typed&limit=7");
        Assert.Equal("typed:7:", overrideValue!.Name);
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
    public async Task BusinessGroup_PartialRuleOutranksDeeperFilterDefaults_WithoutReplacingOtherTargets()
    {
        await using var app = CreateApp(deny: true);
        var root = app.MapGroup("/api").ConfigureProblem(problem => problem.Extension("trace", (_, context) => context.Http.TraceIdentifier));
        var service = root.MapGroup("/service").Response<RateFailure>(output => output.Header("X-Source", "service"));
        var detail = service.MapGroup("/detail");
        detail.Filter<RateFilter>().Response<RateFailure>(output => output.StatusCode(429).Header("X-Source", "filter").Header("Retry-After", 5));
        detail.MapPut("/{id:long}", Denied);
        app.MapOpenApi();
        await app.StartAsync();
        using var client = app.GetTestClient();
        using var response = await client.PutAsJsonAsync("/api/service/detail/1", new { name = "a" });
        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.Equal("service", response.Headers.GetValues("X-Source").Single());
        Assert.Equal("5", response.Headers.GetValues("Retry-After").Single());
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("000429", body.RootElement.GetProperty("title").GetString());
        Assert.Equal("about:blank", body.RootElement.GetProperty("type").GetString());
        Assert.False(body.RootElement.TryGetProperty("code", out _));
        Assert.True(body.RootElement.TryGetProperty("trace", out _));
        Assert.Equal(0, app.Services.GetRequiredService<Probe>().Constructed);
    }

    [Fact]
    public async Task ExcludedFilters_ContributeNeitherFailureDocsNorRuntime_AndGroupRulesAreOnlyCandidates()
    {
        await using var app = CreateApp(deny: true, exclude: true);
        var root = app.MapGroup("/api");
        root.Filter<RateFilter>().Response<RateFailure>(output => output.StatusCode(429));
        root.Response<RateFailure>(output => output.Header("X-Unused", "unused"));
        root.MapPut("/{id:long}", Denied);
        app.MapOpenApi();
        await app.StartAsync();
        using var client = app.GetTestClient();
        using var response = await client.PutAsJsonAsync("/api/1", new { name = "a" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(response.Headers.Contains("X-Unused"));
        using var document = JsonDocument.Parse(await client.GetStringAsync("/openapi/v1.json"));
        var responses = document.RootElement.GetProperty("paths").GetProperty("/api/{id}").GetProperty("put").GetProperty("responses");
        Assert.False(responses.TryGetProperty("429", out _));
        Assert.False(responses.TryGetProperty("422", out _));
    }

    [Fact]
    public async Task Context_IsExplicitAndDoesNotReturnBusinessViewsOrBulkHeaders()
    {
        await using var app = CreateApp(normalize: true);
        app.MapPut("/items/{id:long}", Update).Context(context => context.TenantFromHeader("X-Tenant").Item("correlation").FromHeader("X-Correlation")).Response<UpdateReply>(output => output.Header("X-Boundary-Tenant", (_, context) => context.Context.TenantId));
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

    [Fact]
    public async Task NamedVariants_ResolveFinalDefinitions_AndHeadersMergeCaseInsensitively()
    {
        await using var app = CreateApp();
        var endpoint = app.MapPut("/items/{id:long}", Update).Response<UpdateReply>("pending", output => output.StatusCode(202).Header("X-Test", "old")).Response<UpdateReply>("ready", output => output.StatusCode(201)).SelectResponse((value, mappings) => value is UpdateReply { Id: 1 } ? mappings.Get<UpdateReply>("ready") : mappings.Get<UpdateReply>("pending"));
        endpoint.Response<UpdateReply>("pending", output => output.Header("x-test", "new"));
        await app.StartAsync();
        using var client = app.GetTestClient();
        using var response = await client.PutAsJsonAsync("/items/2", new { name = "a" });
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal("new", response.Headers.GetValues("X-Test").Single());
    }

    [Fact]
    public async Task ProblemFactory_ReplacesEarlierPatches_AndLaterFieldsCanOverrideIt()
    {
        await using var app = CreateApp(deny: true);
        var root = app.MapGroup("/api").ConfigureProblem(problem => problem.Title("root"));
        root.MapPut("/{id:long}", Denied).Response<RateFailure>(output => output.Problem(value => new ProblemDetails { Title = "factory", Detail = value.Detail })).Response<RateFailure>(output => output.ConfigureProblem(problem => problem.Detail("later")));
        await app.StartAsync();
        using var client = app.GetTestClient();
        using var response = await client.PutAsJsonAsync("/api/1", new { name = "a" });
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("factory", body.RootElement.GetProperty("title").GetString());
        Assert.Equal("later", body.RootElement.GetProperty("detail").GetString());
        Assert.Equal("about:blank", body.RootElement.GetProperty("type").GetString());
        Assert.Equal((int)response.StatusCode, body.RootElement.GetProperty("status").GetInt32());
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
    public async Task SameDeclaration_CompleteProblemMasksEarlierFields_AndExtensionNamesRetainCase()
    {
        await using var app = CreateApp(deny: true);
        app.MapPut("/items/{id:long}", Denied).Response<RateFailure>(output => output.ConfigureProblem(problem => problem.Title("masked").Extension("old", true)).Problem(value => new ProblemDetails { Title = "factory", Detail = value.Detail }).ConfigureProblem(problem => problem.Extension("Trace", "upper").Extension("trace", "lower")));
        await app.StartAsync();
        using var client = app.GetTestClient();
        using var response = await client.PutAsJsonAsync("/items/1", new { name = "a" });
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("factory", body.RootElement.GetProperty("title").GetString());
        Assert.False(body.RootElement.TryGetProperty("old", out _));
        Assert.Equal("upper", body.RootElement.GetProperty("Trace").GetString());
        Assert.Equal("lower", body.RootElement.GetProperty("trace").GetString());
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
    public async Task DynamicStatus_RejectsUndeclaredValues_WithoutRunningProviderAtStartup()
    {
        await using var app = CreateApp();
        var calls = 0;
        app.MapPut("/items/{id:long}", Update).Response<UpdateReply>(output => output.StatusCode(_ =>
        {
            calls++;
            return 201;
        }, 200));
        app.MapOpenApi();
        await app.StartAsync();
        using var client = app.GetTestClient();
        _ = await client.GetStringAsync("/openapi/v1.json");
        Assert.Equal(0, calls);
        await Assert.ThrowsAsync<InvalidOperationException>(() => client.PutAsJsonAsync("/items/1", new { name = "a" }));
        Assert.Equal(1, calls);
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

        protected override IReadOnlyDictionary<string, ServiceOperationMapping> GetHandlerMappings() => HandlerMappings;
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
