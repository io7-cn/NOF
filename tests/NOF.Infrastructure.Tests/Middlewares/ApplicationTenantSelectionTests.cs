using Microsoft.Extensions.DependencyInjection;
using NOF.Abstraction;
using NOF.Application;
using NOF.Contract;
using NOF.Hosting;
using NOF.Test;
using System.Security.Claims;
using Xunit;

namespace NOF.Infrastructure.Tests.Middlewares;

public sealed class ApplicationTenantSelectionTests
{
    [Theory]
    [InlineData(nameof(IHostTenantTestService.Host), "host")]
    [InlineData(nameof(IHostTenantTestService.MetadataHost), "trusted")]
    [InlineData(nameof(IHostTenantTestService.Regular), "trusted")]
    [InlineData(nameof(IHostTenantTestService.Disabled), "trusted")]
    public async Task ApplicationFilter_ShouldSelectTenantBeforeResolvingHandlerDependencies(string method, string expectedTenant)
    {
        using var ambient = Context.PushCurrent(Context.Empty.WithTenantId("caller"));
        using var provider = CreateProvider(authenticated: true);
        using var scope = provider.CreateScope();
        var pipeline = scope.ServiceProvider.GetRequiredService<IOperationExecutor>();
        var result = await pipeline.ExecuteAsync(new ServiceOperationDescriptor<Empty, Testing.TestOutcome>(typeof(IHostTenantTestService), method), new Empty(), Context.Empty.WithTenantId("untrusted"), InvocationOrigin.Http);

        Assert.IsType<Empty>(InvocationResult.Value(result));
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
        var result = await scope.ServiceProvider.GetRequiredService<IOperationExecutor>()
            .ExecuteAsync(new ServiceOperationDescriptor<Empty, Testing.TestOutcome>(typeof(IHostTenantTestService), nameof(IHostTenantTestService.Host)), new Empty(), Context.Empty, InvocationOrigin.Http);

        Assert.Equal("Authentication required", Assert.IsType<AuthorizationInvocationFilter.AuthenticationRequired>(result).Title);
        Assert.Null(provider.GetRequiredService<TenantProbe>().DependencyTenant);
    }

    [Fact]
    public async Task LocalRpc_ShouldUseHostTenantAndRestoreCallerOnFailure()
    {
        using var ambient = Context.PushCurrent(Context.Empty.WithTenantId("caller"));
        using var provider = CreateProvider(authenticated: true);
        using var scope = provider.CreateScope();
        var result = await scope.ServiceProvider.GetRequiredService<IOperationInvoker>().InvokeAsync(new ServiceOperationDescriptor<Empty, Testing.TestOutcome>(typeof(IHostTenantTestService), nameof(IHostTenantTestService.Host)), new Empty(),
            Context.Empty.WithTenantId("explicit"), default);
        Assert.IsType<Empty>(InvocationResult.Value(result));
        Assert.Equal(NOFAbstractionConstants.Tenant.HostId, provider.GetRequiredService<TenantProbe>().DependencyTenant);
        Assert.Equal("caller", Context.Current.TenantId);

        provider.GetRequiredService<TenantProbe>().Throw = true;
        var failed = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await scope.ServiceProvider.GetRequiredService<IOperationInvoker>().InvokeAsync(new ServiceOperationDescriptor<Empty, Testing.TestOutcome>(typeof(IHostTenantTestService), nameof(IHostTenantTestService.Host)),
                new Empty(), Context.Empty, default));
        Assert.Equal("Simulated handler failure.", failed.Message);
        Assert.Equal("caller", Context.Current.TenantId);
    }

    private static ServiceProvider CreateProvider(bool authenticated)
    {
        var builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder().AddNOFTest();
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
        registry.Add(new ApplicationServiceRegistration(typeof(IHostTenantTestService), typeof(HostTenantTestServer)) { HandlerMappings = HostTenantTestServer.HandlerMappings });
        builder.Services.AddSingleton(registry);
        builder.Services.AddInvocationFilters(filters => filters.UseCase(new ServiceOperationDescriptor<Empty, Testing.TestOutcome>(
            typeof(IHostTenantTestService), nameof(IHostTenantTestService.Host))).Use<ApplicationHostSelectionFilter>(-25));
        return builder.Services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    }
}

public interface IHostTenantTestService : IApplicationService
{
    [RequirePermission]
    Testing.TestOutcome Host(Empty request);

    [Metadata("NOF.TENANT.USE_HOST", "true")]
    Testing.TestOutcome MetadataHost(Empty request);

    Testing.TestOutcome Regular(Empty request);

    [Metadata("NOF.TENANT.USE_HOST", "false")]
    Testing.TestOutcome Disabled(Empty request);
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

public sealed class TenantProbeHandler : ServiceOperation<Empty, Testing.TestOutcome>
{
    private readonly TenantProbe _probe;

    public TenantProbeHandler(TenantDependency dependency, TenantProbe probe)
    {
        _probe = probe;
        probe.DependencyTenant = dependency.TenantId;
    }

    public override Task<Testing.TestOutcome> HandleAsync(Empty request, InvocationContext context, CancellationToken cancellationToken)
    {
        _probe.HandlerTenant = context.TenantId;
        _probe.AmbientTenant = Context.Current.TenantId;
        if (_probe.Throw)
        {
            throw new InvalidOperationException("Simulated handler failure.");
        }

        return Task.FromResult(NOF.Testing.TestOutcome.Success());
    }
}

public sealed class HostTenantTestServer : ApplicationService<IHostTenantTestService>
{
    public static IReadOnlyDictionary<string, ServiceOperationMapping> HandlerMappings => _mappings;
    private static readonly IReadOnlyDictionary<string, ServiceOperationMapping> _mappings =
        new Dictionary<string, ServiceOperationMapping>
        {
            [nameof(IHostTenantTestService.Host)] = new(typeof(TenantProbeHandler), typeof(Empty), typeof(Testing.TestOutcome)),
            [nameof(IHostTenantTestService.MetadataHost)] = new(typeof(TenantProbeHandler), typeof(Empty), typeof(Testing.TestOutcome)),
            [nameof(IHostTenantTestService.Regular)] = new(typeof(TenantProbeHandler), typeof(Empty), typeof(Testing.TestOutcome)),
            [nameof(IHostTenantTestService.Disabled)] = new(typeof(TenantProbeHandler), typeof(Empty), typeof(Testing.TestOutcome)),
        };

}

/// <summary>An application-owned explicit tenant selection rule, using the ordinary extension boundary.</summary>
public sealed class ApplicationHostSelectionFilter : IInvocationFilter
{
    public async ValueTask<object?> InvokeAsync(InvocationContext context, object input, InvocationDelegate next, CancellationToken ct)
    {
        var host = context.WithTenantId("host");
        using var binding = Context.PushCurrent(host.Context);
        return await next(host, input, ct);
    }
}
