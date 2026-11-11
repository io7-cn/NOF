using Microsoft.Extensions.DependencyInjection;
using NOF.Abstraction;
using NOF.Application;
using NOF.Contract;
using NOF.Hosting;
using NOF.Test;
using System.Security.Claims;
using Xunit;

namespace NOF.Infrastructure.Tests.Middlewares;

public sealed class HostTenantInboundMiddlewareTests
{
    [Theory]
    [InlineData(nameof(IHostTenantTestService.Host), "host")]
    [InlineData(nameof(IHostTenantTestService.MetadataHost), "host")]
    [InlineData(nameof(IHostTenantTestService.Regular), "trusted")]
    [InlineData(nameof(IHostTenantTestService.Disabled), "trusted")]
    public async Task DefaultPipeline_ShouldSelectTenantBeforeResolvingHandlerDependencies(string method, string expectedTenant)
    {
        using var ambient = Context.PushCurrent(Context.Empty.WithTenantId("caller"));
        using var provider = CreateProvider(authenticated: true);
        using var scope = provider.CreateScope();
        var pipeline = scope.ServiceProvider.GetRequiredService<RequestInboundPipelineExecutor>();
        var result = await pipeline.ExecuteAsync(new Empty(), typeof(TenantProbeHandler), typeof(Result),
            typeof(IHostTenantTestService), method,
            [new(NOFAbstractionConstants.Transport.Headers.TenantId, "untrusted")], default);

        Assert.IsType<Success>(Assert.IsType<Result>(result.Response).Value);
        var probe = provider.GetRequiredService<TenantProbe>();
        Assert.Equal(expectedTenant, probe.DependencyTenant);
        Assert.Equal(expectedTenant, probe.HandlerTenant);
        Assert.Equal(expectedTenant, probe.AmbientTenant);
        Assert.Equal("caller", Context.Current.TenantId);
    }

    [Fact]
    public async Task HostTenant_ShouldNotBypassAuthorization()
    {
        using var provider = CreateProvider(authenticated: false);
        using var scope = provider.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<RequestInboundPipelineExecutor>()
            .ExecuteAsync(new Empty(), typeof(TenantProbeHandler), typeof(Result), typeof(IHostTenantTestService),
                nameof(IHostTenantTestService.Host), null, default);

        Assert.Equal("401", Assert.IsType<Failure>(result.Rejection).Code);
        Assert.Null(provider.GetRequiredService<TenantProbe>().DependencyTenant);
    }

    [Fact]
    public async Task LocalRpc_ShouldUseHostTenantAndRestoreCallerOnFailure()
    {
        using var ambient = Context.PushCurrent(Context.Empty.WithTenantId("caller"));
        using var provider = CreateProvider(authenticated: true);
        using var scope = provider.CreateScope();
        var result = await ApplicationServiceInvoker.InvokeAsync<IHostTenantTestService>(scope.ServiceProvider,
            typeof(IHostTenantTestService).GetMethod(nameof(IHostTenantTestService.Host))!, new Empty(),
            Context.Empty.WithTenantId("explicit"), default);
        Assert.IsType<Success>(result.Require<Result>(Result.CreateFailure).Value);
        Assert.Equal(NOFAbstractionConstants.Tenant.HostId, provider.GetRequiredService<TenantProbe>().DependencyTenant);
        Assert.Equal("caller", Context.Current.TenantId);

        provider.GetRequiredService<TenantProbe>().Throw = true;
        var failed = await ApplicationServiceInvoker.InvokeAsync<IHostTenantTestService>(
            scope.ServiceProvider, typeof(IHostTenantTestService).GetMethod(nameof(IHostTenantTestService.Host))!,
            new Empty(), Context.Empty, default);
        Assert.NotNull(Assert.IsType<ServiceRejected>(failed.Value).Failure);
        Assert.Equal("caller", Context.Current.TenantId);
    }

    private static ServiceProvider CreateProvider(bool authenticated)
    {
        var builder = NOFTestAppBuilder.Create();
        var user = new UserContext();
        if (authenticated)
        {
            user.User.AddIdentity(new ClaimsIdentity([
                new Claim(System.Security.Claims.ClaimTypes.NameIdentifier, "user"),
                new Claim(ClaimTypes.TenantId, "trusted")], "test"));
        }

        builder.Services.AddSingleton<IUserContext>(user);
        builder.Services.AddSingleton<TenantProbe>();
        builder.Services.AddScoped<TenantDependency>();
        builder.Services.AddScoped<TenantProbeHandler>();
        builder.Services.AddScoped<HostTenantTestServer>();
        var registry = new ApplicationServiceRegistry();
        registry.Add(new ApplicationServiceRegistration(typeof(IHostTenantTestService), typeof(HostTenantTestServer)));
        builder.Services.AddSingleton(registry);
        return builder.Services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }
}

[TransportOverMemory]
public interface IHostTenantTestService : IApplicationService
{
    [UseHostTenant]
    [RequirePermission]
    Result Host(Empty request);

    [Metadata("NOF.TENANT.USE_HOST", "true")]
    Result MetadataHost(Empty request);

    Result Regular(Empty request);

    [Metadata(UseHostTenantAttribute.MetadataKey, "false")]
    Result Disabled(Empty request);
}



public sealed class TenantProbe
{
    public string? DependencyTenant { get; set; }
    public string? HandlerTenant { get; set; }
    public string? AmbientTenant { get; set; }
    public bool Throw { get; set; }
}

public sealed class TenantDependency
{
    public string TenantId { get; } = Context.Current.TenantId;
}

public sealed class TenantProbeHandler : ServiceOperation<Empty, Result>
{
    private readonly TenantProbe _probe;

    public TenantProbeHandler(TenantDependency dependency, TenantProbe probe)
    {
        _probe = probe;
        probe.DependencyTenant = dependency.TenantId;
    }

    public override Task<Result> HandleAsync(Empty request, Context context, CancellationToken cancellationToken)
    {
        _probe.HandlerTenant = context.TenantId;
        _probe.AmbientTenant = Context.Current.TenantId;
        if (_probe.Throw)
        {
            throw new InvalidOperationException("Simulated handler failure.");
        }

        return Task.FromResult(Result.Success());
    }
}

public sealed class HostTenantTestServer : ApplicationService<IHostTenantTestService>
{
    private static readonly IReadOnlyDictionary<string, ServiceOperationMapping> _mappings =
        new Dictionary<string, ServiceOperationMapping>
        {
            [nameof(IHostTenantTestService.Host)] = new(typeof(TenantProbeHandler), typeof(Empty), typeof(Result))
        };

    protected override IReadOnlyDictionary<string, ServiceOperationMapping> GetHandlerMappings() => _mappings;
}
