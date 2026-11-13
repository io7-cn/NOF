using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using NOF.Application;
using NOF.Contract;
using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace NOF.Hosting.AspNetCore.Tests;

public sealed class HttpEndpointModuleTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OnlyExplicitModule_ShouldExposeHttpRoutes(bool registerModule)
    {
        var builder = WebApplication.CreateBuilder([]).AddNOF();
        builder.WebHost.UseTestServer();
        builder.AddApplicationService<EchoService>();
        builder.Services.AddApplicationService<EchoService>();
        builder.Services.AddScoped<EchoHandler>();
        if (registerModule)
        {
            builder.Services.AddSingleton<IHttpEndpointModule, EchoEndpoints>();
        }
        await using var app = builder.Build();
        app.UseNOF();
        app.MapNOFEndpoints();
        await app.StartAsync();
        using var client = app.GetTestClient();
        foreach (var path in new[] { "/Echo", "/rpc", "/rpc/Echo" })
        {
            using var missing = await client.PostAsJsonAsync(path, new EchoRequest("hello"));
            Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        }
        using var response = await client.PostAsJsonAsync("/api/echoes", new EchoRequest("hello"));
        Assert.Equal(registerModule ? HttpStatusCode.OK : HttpStatusCode.NotFound, response.StatusCode);
        if (registerModule)
        {
            Assert.Equal(new EchoResponse("hello"), await response.Content.ReadFromJsonAsync<EchoResponse>());
            Assert.Single(((IEndpointRouteBuilder)app).DataSources.SelectMany(source => source.Endpoints)
                .OfType<RouteEndpoint>(), endpoint => endpoint.RoutePattern.RawText == "/api/echoes");
        }
    }

    [Fact]
    public void RuntimeAssemblies_ShouldExposeNoLegacyProtocolSurface()
    {
        var removed = new[] { "HttpEndpointAttribute", "TransportOverAttribute", "TransportOverHttpAttribute",
            "TransportOverMemoryAttribute", "HttpRpcStyle", "HttpVerb", "AspNetCoreApplicationServiceTransport",
            "GeneratedHttpRpcEndpoint", "GeneratedHttpRpcRoute", "JsonRpcEndpointHandler", "RpcStreamResponse",
            "RpcHttpResult", "RpcStreamingHttpResult`1", "JsonRpcStreamingHttpResult`1", "NofRpcHttpEndpointMetadata",
            "HttpRpcTransportResultReader", "JsonRpcHttpClient", "RpcResponse", "SseResponseReader",
            "StreamingResponseMetadata", "IApplicationServiceTransport", "ApplicationServiceTransportInitializationStep" };
        foreach (var assembly in new[] { typeof(IHttpEndpointModule).Assembly, typeof(IApplicationService).Assembly,
            typeof(NOF.Infrastructure.NOFInfrastructureConstants).Assembly })
        {
            Assert.DoesNotContain(assembly.GetTypes(), type => removed.Contains(type.Name));
        }
    }

    public sealed record EchoRequest(string Value);
    public sealed record EchoResponse(string Value);
    public interface IEchoService : IApplicationService
    {
        EchoResponse Echo(EchoRequest request);
    }
    public sealed class EchoService : ApplicationService<IEchoService>, IApplicationServiceImplementation
    {
        public static IReadOnlyDictionary<string, ServiceOperationMapping> HandlerMappings { get; } =
            new Dictionary<string, ServiceOperationMapping>
            {
                [nameof(IEchoService.Echo)] = new(typeof(EchoHandler), typeof(EchoRequest), typeof(EchoResponse))
            };
        protected override IReadOnlyDictionary<string, ServiceOperationMapping> GetHandlerMappings() => HandlerMappings;
    }
    public sealed class EchoHandler : ServiceOperation<EchoRequest, EchoResponse>
    {
        public override Task<EchoResponse> HandleAsync(EchoRequest request, Context context, CancellationToken cancellationToken)
            => Task.FromResult(new EchoResponse(request.Value));
    }
    public sealed class EchoEndpoints : IHttpEndpointModule
    {
        public void Map(IEndpointRouteBuilder endpoints)
            => endpoints.MapPost("/api/echoes", async (EchoRequest request, HttpContext http, CancellationToken ct)
                => TypedResults.Ok(await http.InvokeAsync(new ServiceOperationDescriptor<EchoRequest, EchoResponse>(
                    typeof(IEchoService), nameof(IEchoService.Echo)), request, ct)))
                .AddEndpointFilter<ServiceRejectionHttpFilter>();
    }
}
