using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NOF.Hosting;
using Xunit;

namespace NOF.Hosting.AspNetCore.Tests;

public sealed class NativeHostingTests
{
    [Fact]
    public async Task Registration_PreservesNativeEnvironmentAndPerHostOptions()
    {
        var first = WebApplication.CreateBuilder([]);
        var environment = first.Services.Single(descriptor => descriptor.ServiceType == typeof(IHostEnvironment));
        first.AddNOF().AddNOF();
        first.Configuration["ServiceName"] = "orders";
        first.Services.Configure<DeploymentOptions>(options =>
        {
            options.InstanceId = 7;
            options.PrimaryNode = true;
        });
        await using var app = first.Build();
        var deployment = app.Services.GetRequiredService<IOptions<DeploymentOptions>>().Value;
        Assert.Contains(environment, first.Services);
        Assert.Same(environment.ImplementationInstance, app.Services.GetRequiredService<IHostEnvironment>());
        Assert.Equal("orders", deployment.ServiceName);
        Assert.Equal(7u, deployment.InstanceId);
        Assert.True(deployment.IsPrimaryNode);

        var second = WebApplication.CreateBuilder([]).AddNOF();
        await using var other = second.Build();
        var otherDeployment = other.Services.GetRequiredService<IOptions<DeploymentOptions>>().Value;
        Assert.Equal(1u, otherDeployment.InstanceId);
        Assert.NotEqual("orders", otherDeployment.ServiceName);
        Assert.Single(first.Services, descriptor => descriptor.ImplementationType == typeof(Infrastructure.InboxMessageBackgroundService));
    }

    [Fact]
    public async Task ApplicationParts_AreExplicitAndIdempotentAcrossRegistrationEntryPoints()
    {
        var builder = WebApplication.CreateBuilder([]).AddNOF();
        Assert.DoesNotContain(builder.Services, descriptor => descriptor.ServiceType == typeof(__TypeResolverSynchronizationProbe));
        builder.AddApplicationPart(typeof(__TypeResolverSynchronizationProbe).Assembly);
        builder.Services.AddApplicationPart(typeof(__TypeResolverSynchronizationProbe).Assembly);
        await using var app = builder.Build();
        Assert.Single(app.Services.GetServices<__TypeResolverSynchronizationProbe>());
    }

    [Fact]
    public async Task HttpComposition_IsExplicitAndMapsModulesOnce()
    {
        var builder = WebApplication.CreateBuilder([]).AddNOF();
        builder.WebHost.UseTestServer();
        var module = new CountingModule();
        builder.Services.AddSingleton<IHttpEndpointModule>(module);
        await using var app = builder.Build();
        Assert.Equal(0, module.Calls);
        app.UseNOF().UseNOF();
        app.MapNOFEndpoints().MapNOFEndpoints();
        Assert.Equal(1, module.Calls);
        await app.StartAsync();
        using var client = app.GetTestClient();
        Assert.Equal("ready", await client.GetStringAsync("/native"));
    }

    public sealed class CountingModule : IHttpEndpointModule
    {
        public int Calls { get; private set; }
        public void Map(Microsoft.AspNetCore.Routing.IEndpointRouteBuilder endpoints)
        {
            Calls++;
            endpoints.MapGet("/native", () => "ready");
        }
    }
}
