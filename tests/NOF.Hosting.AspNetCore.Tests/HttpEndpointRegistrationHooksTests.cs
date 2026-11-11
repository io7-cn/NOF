using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using NOF.Application;
using NOF.Contract;
using System.Net;
using System.Net.Http.Json;
using Xunit;

namespace NOF.Hosting.AspNetCore.Tests;

public sealed class HttpEndpointRegistrationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AddApplicationService_ShouldMapRpcEndpointAutomatically(bool registerThroughServices)
    {
        var builder = NOFWebApplicationBuilder.Create([]);
        builder.WebApplicationBuilder.WebHost.UseTestServer();
        if (registerThroughServices)
        {
            builder.Services.AddApplicationService<HookedApplicationService>();
        }
        else
        {
            builder.AddApplicationService<HookedApplicationService>();
        }
        builder.Services.AddTransient<EchoHandler>();

        await using var app = await builder.BuildAsync();
        await app.StartAsync();

        using var client = app.GetTestClient();
        using var response = await client.PostAsJsonAsync("/Echo", new EchoRequest
        {
            Value = "hello"
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<Result<EchoResponse>>();
        Assert.NotNull(payload.ExpectSuccess());
        Assert.Equal("hello", payload.ExpectSuccess()!.Value);
    }

    [Fact]
    public async Task AddApplicationService_ShouldRegisterApplicationServiceOnlyOnce()
    {
        var builder = NOFWebApplicationBuilder.Create([]);
        builder.WebApplicationBuilder.WebHost.UseTestServer();
        builder.AddApplicationService<HookedApplicationService>();
        builder.Services.AddApplicationService<HookedApplicationService>();
        builder.Services.AddTransient<EchoHandler>();

        await using var app = await builder.BuildAsync();
        await app.StartAsync();

        var routeEndpoints = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(static dataSource => dataSource.Endpoints)
            .OfType<RouteEndpoint>()
            .Where(static endpoint => string.Equals(endpoint.RoutePattern.RawText, "/Echo", StringComparison.Ordinal))
            .ToArray();

        Assert.Single(routeEndpoints);
    }

    [Fact]
    public async Task AddApplicationService_WithMemoryTransport_ShouldNotMapHttpEndpoint()
    {
        var builder = NOFWebApplicationBuilder.Create([]);
        builder.WebApplicationBuilder.WebHost.UseTestServer();
        builder.AddApplicationService<MemoryApplicationService>();
        builder.Services.AddTransient<MemoryEchoHandler>();

        await using var app = await builder.BuildAsync();
        await app.StartAsync();

        var routeEndpoints = ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(static dataSource => dataSource.Endpoints)
            .OfType<RouteEndpoint>()
            .Where(static endpoint => string.Equals(endpoint.RoutePattern.RawText, "/MemoryEcho", StringComparison.Ordinal))
            .ToArray();

        Assert.Empty(routeEndpoints);

        using var client = app.GetTestClient();
        using var response = await client.PostAsJsonAsync("/MemoryEcho", new EchoRequest
        {
            Value = "local only"
        });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task AddApplicationService_ShouldUseControllerRpcContractRoutePrefix()
    {
        var builder = NOFWebApplicationBuilder.Create([]);
        builder.WebApplicationBuilder.WebHost.UseTestServer();
        builder.AddApplicationService<PrefixedControllerApplicationService>();
        builder.Services.AddTransient<PrefixedEchoHandler>();

        await using var app = await builder.BuildAsync();
        await app.StartAsync();

        using var client = app.GetTestClient();
        using var response = await client.PostAsJsonAsync("/controller-rpc/Echo", new EchoRequest
        {
            Value = "prefixed"
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var payload = await response.Content.ReadFromJsonAsync<Result<EchoResponse>>();
        Assert.NotNull(payload.ExpectSuccess());
        Assert.Equal("prefixed", payload.ExpectSuccess()!.Value);
    }

    public sealed class EchoRequest
    {
        public string Value { get; set; } = string.Empty;
    }

    public sealed record EchoResponse(string Value);

    [TransportOverHttp(HttpRpcStyle.ControllerRpc)]
    public partial interface IHookedRpcService : IApplicationService
    {
        [HttpEndpoint(HttpVerb.Post, "/Echo")]
        Result<EchoResponse> Echo(EchoRequest request);
    }

    public sealed class HookedApplicationService : ApplicationService<IHookedRpcService>, IApplicationServiceImplementation
    {
        public static IReadOnlyDictionary<string, ServiceOperationMapping> HandlerMappings { get; } =
            new Dictionary<string, ServiceOperationMapping>
            {
                [nameof(IHookedRpcService.Echo)] =
                    new(typeof(EchoHandler), typeof(EchoRequest), typeof(Result<EchoResponse>))
            };

        protected override IReadOnlyDictionary<string, ServiceOperationMapping> GetHandlerMappings() => HandlerMappings;
    }

    public sealed class EchoHandler : ServiceOperation<EchoRequest, Result<EchoResponse>>
    {
        public override Task<Result<EchoResponse>> HandleAsync(EchoRequest request, Context context, CancellationToken cancellationToken)
            => Task.FromResult(Result.Success(new EchoResponse(request.Value)));
    }

    [TransportOverHttp(HttpRpcStyle.ControllerRpc, "controller-rpc/")]
    public partial interface IPrefixedControllerRpcService : IApplicationService
    {
        [HttpEndpoint(HttpVerb.Post, "/Echo")]
        Result<EchoResponse> Echo(EchoRequest request);
    }

    public sealed class PrefixedControllerApplicationService : ApplicationService<IPrefixedControllerRpcService>, IApplicationServiceImplementation
    {
        public static IReadOnlyDictionary<string, ServiceOperationMapping> HandlerMappings { get; } =
            new Dictionary<string, ServiceOperationMapping>
            {
                [nameof(IPrefixedControllerRpcService.Echo)] =
                    new(typeof(PrefixedEchoHandler), typeof(EchoRequest), typeof(Result<EchoResponse>))
            };

        protected override IReadOnlyDictionary<string, ServiceOperationMapping> GetHandlerMappings() => HandlerMappings;
    }

    public sealed class PrefixedEchoHandler : ServiceOperation<EchoRequest, Result<EchoResponse>>
    {
        public override Task<Result<EchoResponse>> HandleAsync(EchoRequest request, Context context, CancellationToken cancellationToken)
            => Task.FromResult(Result.Success(new EchoResponse(request.Value)));
    }

    [TransportOverMemory]
    public interface IMemoryRpcService : IApplicationService
    {
        Result<EchoResponse> MemoryEcho(EchoRequest request);
    }

    public sealed class MemoryApplicationService : ApplicationService<IMemoryRpcService>, IApplicationServiceImplementation
    {
        public static IReadOnlyDictionary<string, ServiceOperationMapping> HandlerMappings { get; } =
            new Dictionary<string, ServiceOperationMapping>
            {
                [nameof(IMemoryRpcService.MemoryEcho)] =
                    new(typeof(MemoryEchoHandler), typeof(EchoRequest), typeof(Result<EchoResponse>))
            };

        protected override IReadOnlyDictionary<string, ServiceOperationMapping> GetHandlerMappings() => HandlerMappings;
    }

    public sealed class MemoryEchoHandler : ServiceOperation<EchoRequest, Result<EchoResponse>>
    {
        public override Task<Result<EchoResponse>> HandleAsync(EchoRequest request, Context context, CancellationToken cancellationToken)
            => Task.FromResult(Result.Success(new EchoResponse(request.Value)));
    }
}
