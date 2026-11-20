using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using NOF.Application;
using NOF.Contract;
using NOF.Infrastructure;
using System.Text.Json;
using Xunit;

namespace NOF.Hosting.AspNetCore.Tests;

public sealed class HttpResponseNullabilityTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task ObjectAndCollectionResponsesPreserveNullablePropertiesWithoutChangingSharedTypes(bool stringInt64, bool childFirst)
    {
        var builder = WebApplication.CreateBuilder([]).AddNOF();
        builder.WebHost.UseTestServer();
        if (stringInt64)
        {
            builder.ConfigureNOFJson(json => json.UseStringInt64());
        }
        Register<Child, Detail>(builder);
        Register<One, Entry>(builder);
        Register<Many, IReadOnlyList<Entry>>(builder);
        await using var app = builder.Build();
        app.UseNOF();
        app.MapGet<One>("/projected")
            .Response((Entry entry) => (Results<Ok<Entry>, Ok<Detail>>)TypedResults.Ok(entry));
        if (childFirst)
        {
            app.MapGet<Child>("/child");
        }
        app.MapGet<One>("/one");
        app.MapGet<Many>("/many");
        if (!childFirst)
        {
            app.MapGet<Child>("/child");
        }
        app.MapGet("/native/child", () => new Detail("example", DateTimeOffset.UnixEpoch));
        app.MapGet("/native/one", () => new Entry(1, null));
        app.MapGet("/native/many", () => (IReadOnlyList<Entry>)[new(1, null)]);
        app.MapOpenApi();
        await app.StartAsync();
        using var client = app.GetTestClient();

        foreach (var path in new[] { "/one", "/many", "/native/one", "/native/many" })
        {
            using var response = JsonDocument.Parse(await client.GetStringAsync(path));
            var entry = path.EndsWith("many", StringComparison.Ordinal) ? response.RootElement[0] : response.RootElement;
            Assert.Equal(JsonValueKind.Null, entry.GetProperty("detail").ValueKind);
            Assert.Equal(stringInt64 ? JsonValueKind.String : JsonValueKind.Number, entry.GetProperty("id").ValueKind);
        }

        for (var generation = 0; generation < 2; generation++)
        {
            using var document = JsonDocument.Parse(await client.GetStringAsync("/openapi/v1.json"));
            foreach (var prefix in new[] { "", "/native" })
            {
                var one = Resolve(document, ResponseSchema(document, $"{prefix}/one"));
                var many = Resolve(document, Resolve(document, ResponseSchema(document, $"{prefix}/many")).GetProperty("items"));
                foreach (var entry in new[] { one, many })
                {
                    var detail = entry.GetProperty("properties").GetProperty("detail");
                    Assert.True(AllowsNull(document, detail), detail.GetRawText());
                    Assert.Contains(entry.GetProperty("required").EnumerateArray(), property => property.GetString() == "detail");
                }
                var child = ResponseSchema(document, $"{prefix}/child");
                Assert.False(AllowsNull(document, child), child.GetRawText());
                Assert.True(Resolve(document, child).GetProperty("properties").TryGetProperty("text", out _));
            }
            var component = document.RootElement.GetProperty("components").GetProperty("schemas").GetProperty("Detail");
            Assert.False(AllowsNull(document, component), component.GetRawText());
            var projected = ResponseSchema(document, "/projected").GetProperty("anyOf").EnumerateArray()
                .Select(schema => Resolve(document, schema)).ToArray();
            var projectedEntry = projected.Single(schema => schema.GetProperty("properties").TryGetProperty("id", out _));
            Assert.True(AllowsNull(document, projectedEntry.GetProperty("properties").GetProperty("detail")), projectedEntry.GetRawText());
            var projectedDetail = projected.Single(schema => schema.GetProperty("properties").TryGetProperty("text", out _));
            Assert.False(AllowsNull(document, projectedDetail), projectedDetail.GetRawText());
        }
    }

    private static void Register<THandler, TResponse>(WebApplicationBuilder builder)
        where THandler : OperationHandler<Empty, TResponse>
    {
        builder.Services.AddScoped<THandler>();
        var descriptor = new OperationDescriptor<Empty, TResponse>(typeof(THandler));
        builder.Services.GetOrAddSingleton<ApplicationServiceRegistry>()
            .AddOperation(new(typeof(THandler), typeof(Empty), typeof(TResponse)) { Descriptor = descriptor });
    }

    private static JsonElement ResponseSchema(JsonDocument document, string path)
        => document.RootElement.GetProperty("paths").GetProperty(path).GetProperty("get")
            .GetProperty("responses").GetProperty("200").GetProperty("content").GetProperty("application/json").GetProperty("schema");

    private static JsonElement Resolve(JsonDocument document, JsonElement schema)
        => schema.TryGetProperty("$ref", out var reference)
            ? Resolve(document, document.RootElement.GetProperty("components").GetProperty("schemas").GetProperty(reference.GetString()!.Split('/')[^1]))
            : schema;

    private static bool AllowsNull(JsonDocument document, JsonElement schema)
    {
        schema = Resolve(document, schema);
        if (schema.TryGetProperty("type", out var type))
        {
            return type.ValueKind == JsonValueKind.Array
                ? type.EnumerateArray().Any(value => value.GetString() == "null")
                : type.GetString() == "null";
        }
        return (schema.TryGetProperty("anyOf", out var alternatives) || schema.TryGetProperty("oneOf", out alternatives))
            && alternatives.EnumerateArray().Any(value => AllowsNull(document, value));
    }

    public sealed record Entry(long Id, Detail? Detail);
    public sealed record Detail(string Text, DateTimeOffset At);

    public sealed class Child : OperationHandler<Empty, Detail>
    {
        public override Task<Detail> HandleAsync(Empty request, InvocationContext context, CancellationToken cancellationToken)
            => Task.FromResult(new Detail("example", DateTimeOffset.UnixEpoch));
    }

    public sealed class One : OperationHandler<Empty, Entry>
    {
        public override Task<Entry> HandleAsync(Empty request, InvocationContext context, CancellationToken cancellationToken)
            => Task.FromResult(new Entry(1, null));
    }

    public sealed class Many : OperationHandler<Empty, IReadOnlyList<Entry>>
    {
        public override Task<IReadOnlyList<Entry>> HandleAsync(Empty request, InvocationContext context, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<Entry>>([new(1, null)]);
    }
}
