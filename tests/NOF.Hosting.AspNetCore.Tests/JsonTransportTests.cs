using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using NOF.Abstraction;
using NOF.Application;
using NOF.Infrastructure;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Xunit;

namespace NOF.Hosting.AspNetCore.Tests;

public sealed class JsonTransportTests
{
    [Fact]
    public async Task StringTransportAlignsHttpPayloadsParametersDefaultsAndOpenApi()
    {
        await using var app = CreateApp(strings: true);
        app.MapPost<Echo>("/body");
        app.MapPost<Echo>("/whole").Request(request => request.Body<Payload>().Map(body => body));
        app.MapGet<Echo>("/query/{id}")
            .Request(request => request.Member(value => value.State).FromQuery("state", State.Ready));
        app.MapGet<Echo>("/header/{id}")
            .Request(request => request.Member(value => value.State).FromHeader("X-State"));
        app.MapPost("/native", (Payload payload) => TypedResults.Ok(payload));
        app.MapOpenApi();
        await app.StartAsync();
        using var client = app.GetTestClient();
        const string Body = "{\"id\":\"9223372036854775807\",\"state\":\"in-progress\",\"values\":[\"-9223372036854775808\",null],\"count\":3}";
        foreach (var path in new[] { "/body", "/whole", "/native" })
        {
            using var response = await client.PostAsync(path, new StringContent(Body, Encoding.UTF8, "application/json"));
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var result = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal("9223372036854775807", result.RootElement.GetProperty("id").GetString());
            Assert.Equal("in-progress", result.RootElement.GetProperty("state").GetString());
            Assert.Equal("-9223372036854775808", result.RootElement.GetProperty("values")[0].GetString());
            Assert.Equal(JsonValueKind.Number, result.RootElement.GetProperty("count").ValueKind);
        }
        using var query = await client.GetAsync("/query/9223372036854775807?state=in-progress&values=-9223372036854775808&count=3");
        Assert.Equal(HttpStatusCode.OK, query.StatusCode);
        using var queryResult = JsonDocument.Parse(await query.Content.ReadAsStringAsync());
        Assert.Equal("in-progress", queryResult.RootElement.GetProperty("state").GetString());
        using var invalid = await client.GetAsync("/query/1?state=1&count=3");
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        using var headerRequest = new HttpRequestMessage(HttpMethod.Get, "/header/42?count=3");
        headerRequest.Headers.Add("X-State", "in-progress");
        using var headerResponse = await client.SendAsync(headerRequest);
        Assert.Equal(HttpStatusCode.OK, headerResponse.StatusCode);
        using var numericEnum = await client.PostAsync("/body", new StringContent(Body.Replace("\"in-progress\"", "1"), Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, numericEnum.StatusCode);
        using var overflow = await client.PostAsync("/body", new StringContent(Body.Replace("9223372036854775807", "9223372036854775808"), Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, overflow.StatusCode);

        var serializer = app.Services.GetRequiredService<IObjectSerializer>();
        using var message = JsonDocument.Parse(serializer.Serialize(new Payload { Id = long.MaxValue, State = State.InProgress }));
        Assert.Equal("9223372036854775807", message.RootElement.GetProperty("id").GetString());
        Assert.Equal("in-progress", message.RootElement.GetProperty("state").GetString());

        using var spec = JsonDocument.Parse(await client.GetStringAsync("/openapi/v1.json"));
        var paths = spec.RootElement.GetProperty("paths");
        var operation = paths.GetProperty("/query/{id}").GetProperty("get");
        var parameters = operation.GetProperty("parameters").EnumerateArray().ToArray();
        AssertString(spec, parameters.Single(parameter => parameter.GetProperty("name").GetString() == "id").GetProperty("schema"));
        var state = parameters.Single(parameter => parameter.GetProperty("name").GetString() == "state").GetProperty("schema");
        Assert.Equal("ready", state.GetProperty("default").GetString());
        AssertString(spec, state);
        foreach (var path in new[] { "/body", "/whole", "/native" })
        {
            var post = paths.GetProperty(path).GetProperty("post");
            var request = Resolve(spec, post.GetProperty("requestBody").GetProperty("content").GetProperty("application/json").GetProperty("schema"));
            AssertString(spec, request.GetProperty("properties").GetProperty("id"));
            AssertString(spec, request.GetProperty("properties").GetProperty("state"));
            var response = Resolve(spec, post.GetProperty("responses").GetProperty("200").GetProperty("content").GetProperty("application/json").GetProperty("schema"));
            AssertString(spec, response.GetProperty("properties").GetProperty("id"));
            var enumSchema = Resolve(spec, response.GetProperty("properties").GetProperty("state"));
            Assert.Contains(enumSchema.GetProperty("enum").EnumerateArray(), value => value.GetString() == "in-progress");
            var arraySchema = Resolve(spec, response.GetProperty("properties").GetProperty("values"));
            Assert.True(arraySchema.TryGetProperty("items", out var items), arraySchema.GetRawText());
            var itemSchema = Resolve(spec, items);
            Assert.Contains("string", itemSchema.GetProperty("type").EnumerateArray().Select(value => value.GetString()));
            Assert.Contains("null", itemSchema.GetProperty("type").EnumerateArray().Select(value => value.GetString()));
            var dictionary = Resolve(spec, response.GetProperty("properties").GetProperty("totals"));
            var dictionaryValue = Resolve(spec, dictionary.GetProperty("additionalProperties"));
            Assert.Contains("string", dictionaryValue.GetProperty("type").EnumerateArray().Select(value => value.GetString()));
        }
    }

    [Fact]
    public async Task ValueObjectsUsePrimitiveStringSchemasAndNullableWireValues()
    {
        await using var app = CreateApp(strings: true);
        app.MapPost("/ids", (IdPayload payload) => TypedResults.Ok(payload));
        app.MapOpenApi();
        await app.StartAsync();
        using var client = app.GetTestClient();
        using var response = await client.PostAsync("/ids", new StringContent("{\"id\":\"9223372036854775807\",\"optional\":null}", Encoding.UTF8, "application/json"));
        response.EnsureSuccessStatusCode();
        using var result = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("9223372036854775807", result.RootElement.GetProperty("id").GetString());
        Assert.Equal(JsonValueKind.Null, result.RootElement.GetProperty("optional").ValueKind);
        using var spec = JsonDocument.Parse(await client.GetStringAsync("/openapi/v1.json"));
        var schema = Resolve(spec, spec.RootElement.GetProperty("paths").GetProperty("/ids").GetProperty("post")
            .GetProperty("responses").GetProperty("200").GetProperty("content").GetProperty("application/json").GetProperty("schema"));
        var id = Resolve(spec, schema.GetProperty("properties").GetProperty("id"));
        AssertString(spec, id.GetProperty("anyOf")[0]);
        var optional = Resolve(spec, schema.GetProperty("properties").GetProperty("optional"));
        Assert.True(optional.TryGetProperty("anyOf", out var alternatives) || optional.TryGetProperty("oneOf", out alternatives), optional.GetRawText());
        Assert.Contains(alternatives.EnumerateArray(), value => value.TryGetProperty("type", out var type) && type.GetString() == "null");
    }

    [Fact]
    public async Task DefaultsPreserveNumericPayloadsAndHostIsolation()
    {
        await using var app = CreateApp(strings: false);
        app.MapPost<Echo>("/body");
        await app.StartAsync();
        using var client = app.GetTestClient();
        using var response = await client.PostAsync("/body", new StringContent("{\"id\":42,\"state\":1,\"count\":3}", Encoding.UTF8, "application/json"));
        response.EnsureSuccessStatusCode();
        using var result = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(JsonValueKind.Number, result.RootElement.GetProperty("id").ValueKind);
        Assert.Equal(JsonValueKind.Number, result.RootElement.GetProperty("state").ValueKind);
    }

    private static WebApplication CreateApp(bool strings)
    {
        var builder = WebApplication.CreateBuilder([]).AddNOF();
        builder.WebHost.UseTestServer();
        if (strings)
        {
            builder.ConfigureNOFJson(json => json.UseStringInt64().UseStringEnums<State>(JsonNamingPolicy.CamelCase));
        }
        builder.Services.AddScoped<Echo>();
        var descriptor = new OperationDescriptor<Payload, Payload>(typeof(Echo));
        builder.Services.GetOrAddSingleton<ApplicationServiceRegistry>()
            .AddOperation(new(typeof(Echo), typeof(Payload), typeof(Payload)) { Descriptor = descriptor });
        return builder.Build();
    }

    private static JsonElement Resolve(JsonDocument document, JsonElement schema)
    {
        if (schema.TryGetProperty("$ref", out var reference))
        {
            return Resolve(document, document.RootElement.GetProperty("components").GetProperty("schemas").GetProperty(reference.GetString()!.Split('/').Last()));
        }
        if (schema.TryGetProperty("allOf", out var allOf))
        {
            return Resolve(document, allOf[0]);
        }
        return schema;
    }

    private static void AssertString(JsonDocument document, JsonElement schema)
    {
        var resolved = Resolve(document, schema);
        Assert.True(resolved.TryGetProperty("type", out var type), resolved.GetRawText());
        Assert.Equal("string", type.GetString());
    }

    public enum State
    {
        Ready,
        [JsonStringEnumMemberName("in-progress")]
        InProgress
    }

    public sealed record IdPayload(JsonTransportId Id, JsonTransportId? Optional);

    public sealed record Payload
    {
        public long Id { get; init; }
        public State State { get; init; }
        public long?[] Values { get; init; } = [];
        public Dictionary<string, long?> Totals { get; init; } = [];
        public int Count { get; init; }
    }

    public sealed class Echo : OperationHandler<Payload, Payload>
    {
        public override Task<Payload> HandleAsync(Payload request, InvocationContext context, CancellationToken cancellationToken)
            => Task.FromResult(request);
    }
}
