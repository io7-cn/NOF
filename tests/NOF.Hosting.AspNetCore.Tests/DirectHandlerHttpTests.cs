using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using NOF.Abstraction;
using NOF.Application;
using NOF.Infrastructure;
using System.Net.Http.Json;
using Xunit;

namespace NOF.Hosting.AspNetCore.Tests;

public sealed class DirectHandlerHttpTests
{
    [Fact]
    public async Task ProjectedBody_PreservesRequiredNullableMembersWithoutChangingOtherSchemas()
    {
        var builder = WebApplication.CreateBuilder().AddNOF();
        builder.WebHost.UseTestServer();
        builder.Services.AddScoped<Echo>();
        var descriptor = new OperationDescriptor<EchoInput, EchoInput>(typeof(Echo));
        builder.Services.GetOrAddSingleton<ApplicationServiceRegistry>()
            .AddOperation(new(typeof(Echo), typeof(EchoInput), typeof(EchoInput)) { Descriptor = descriptor });
        await using var app = builder.Build();
        app.UseNOF();
        app.MapPost<Echo>("/echo");
        app.MapOpenApi();
        await app.StartAsync();
        using var client = app.GetTestClient();
        using var spec = System.Text.Json.JsonDocument.Parse(await client.GetStringAsync("/openapi/v1.json"));
        var schema = spec.RootElement.GetProperty("paths").GetProperty("/echo").GetProperty("post")
            .GetProperty("requestBody").GetProperty("content").GetProperty("application/json").GetProperty("schema");
        foreach (var name in new[] { "note", "count" })
        {
            Assert.Contains(schema.GetProperty("required").EnumerateArray(), value => value.GetString() == name);
            Assert.Contains(schema.GetProperty("properties").GetProperty(name).GetProperty("anyOf").EnumerateArray(),
                value => value.TryGetProperty("type", out var type) && type.ValueKind == System.Text.Json.JsonValueKind.String && type.GetString() == "null");
        }
        using var accepted = await client.PostAsJsonAsync("/echo", new { note = (string?)null, count = (int?)null });
        Assert.Equal(System.Net.HttpStatusCode.OK, accepted.StatusCode);
        using var missing = await client.PostAsJsonAsync("/echo", new { note = (string?)null });
        Assert.Equal(System.Net.HttpStatusCode.BadRequest, missing.StatusCode);
    }

    public sealed record EchoInput
    {
        public required string? Note { get; init; }
        public required int? Count { get; init; }
    }

    public sealed class Echo : OperationHandler<EchoInput, EchoInput>
    {
        public override Task<EchoInput> HandleAsync(EchoInput input, InvocationContext context, CancellationToken cancellationToken)
            => Task.FromResult(input);
    }

    [Fact]
    public async Task StandaloneReplyHandler_MapsTypedRequestsAndPreservesNullableCompletion()
    {
        var builder = WebApplication.CreateBuilder().AddNOF();
        builder.WebHost.UseTestServer();
        builder.Services.AddScoped<Find>();
        var descriptor = new OperationDescriptor<Query, Reply?>(typeof(Find), allowsNullResponse: true);
        builder.Services.GetOrAddSingleton<ApplicationServiceRegistry>()
            .AddOperation(new(typeof(Find), typeof(Query), typeof(Reply)) { Descriptor = descriptor });
        await using var app = builder.Build();
        app.UseNOF();
        app.MapGet<Find>("/private-lookup").Request(request => request.Member(value => value.Id).FromQuery("key", 5));
        var plans = app.Services.GetRequiredService<IInvocationPlanProvider>();
        Assert.Empty(plans.Describe(descriptor, InvocationOrigin.Http).Authorization!.Authorization.Requirements);
        await app.StartAsync();
        using var client = app.GetTestClient();
        Assert.Equal(new Reply(5), await client.GetFromJsonAsync<Reply>("/private-lookup"));
        using var missing = await client.GetAsync("/private-lookup?key=0");
        Assert.Equal(System.Net.HttpStatusCode.OK, missing.StatusCode);
        Assert.Equal("null", await missing.Content.ReadAsStringAsync());
    }

    public sealed record Query(int Id);
    public sealed record Reply(int Id);
    public sealed class Find : OperationHandler<Query, Reply?>
    {
        public override Task<Reply?> HandleAsync(Query request, InvocationContext context, CancellationToken cancellationToken)
            => Task.FromResult(request.Id == 0 ? null : new Reply(request.Id));
    }
}
