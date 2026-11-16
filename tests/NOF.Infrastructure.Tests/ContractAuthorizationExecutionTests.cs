using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NOF.Abstraction;
using NOF.Application;
using NOF.Contract;
using NOF.Hosting;
using System.Security.Claims;
using Xunit;

namespace NOF.Infrastructure.Tests;

public sealed class ContractAuthorizationExecutionTests
{
    [Theory]
    [InlineData(InvocationOrigin.Http, 0)]
    [InlineData(InvocationOrigin.Http, 1)]
    [InlineData(InvocationOrigin.Http, 2)]
    [InlineData(InvocationOrigin.Local, 0)]
    [InlineData(InvocationOrigin.Local, 1)]
    [InlineData(InvocationOrigin.Local, 2)]
    [InlineData(InvocationOrigin.Message, 0)]
    [InlineData(InvocationOrigin.Message, 1)]
    [InlineData(InvocationOrigin.Message, 2)]
    public async Task EveryEntry_UsesCachedContractPolicy_BeforeConstructingHandler(InvocationOrigin origin, int identity)
    {
        var builder = Configure();
        using var host = builder.Build();
        await host.StartAsync();
        try
        {
            using var scope = host.Services.CreateScope();
            if (identity > 0)
            {
                var principal = new ClaimsIdentity("test");
                if (identity == 2)
                {
                    principal.AddClaim(new(ClaimTypes.Permission, "read"));
                }
                scope.ServiceProvider.GetRequiredService<IUserContext>().User.AddIdentity(principal);
            }
            var descriptor = new OperationDescriptor<Empty, Empty>(typeof(IService), nameof(IService.Read));
            var plans = scope.ServiceProvider.GetRequiredService<IInvocationPlanProvider>();
            var plan = plans.Describe(descriptor, origin);
            var result = await scope.ServiceProvider.GetRequiredService<OperationExecutor>()
                .ExecuteInScopeAsync(descriptor, new Empty(), Context.Empty, origin);
            Assert.Same(plan.Authorization!.Authorization, ((CapturingChecker)scope.ServiceProvider.GetRequiredService<IPermissionChecker>()).Policy);
            Assert.Equal(identity == 2 ? 1 : 0, host.Services.GetRequiredService<Probe>().Constructed);
            if (identity == 0)
            {
                Assert.IsType<AuthorizationInvocationFilter.AuthenticationRequired>(result);
            }
            else if (identity == 1)
            {
                Assert.IsType<AuthorizationInvocationFilter.PermissionDenied>(result);
            }
            else
            {
                Assert.IsType<Empty>(result);
            }
        }
        finally
        {
            await host.StopAsync();
        }
    }

    [Fact]
    public async Task ContractPermission_NeedsNoCatalog_AndStartupNeverQueriesGrants()
    {
        var builder = Configure();
        builder.Services.AddScoped<IPermissionGrantProvider>(_ => throw new InvalidOperationException("Grant source must remain lazy."));
        using var host = builder.Build();
        await host.StartAsync();
        try
        {
            Assert.Equal(0, host.Services.GetRequiredService<Probe>().Constructed);
        }
        finally
        {
            await host.StopAsync();
        }
    }

    [Theory]
    [InlineData(InvocationOrigin.Http)]
    [InlineData(InvocationOrigin.Local)]
    [InlineData(InvocationOrigin.Message)]
    public async Task CachedPlan_DoesNotCacheDynamicGrants(InvocationOrigin origin)
    {
        var builder = Configure();
        var grants = new DynamicGrants();
        builder.Services.AddScoped<IPermissionGrantProvider>(_ => grants);
        using var host = builder.Build();
        await host.StartAsync();
        try
        {
            using var scope = host.Services.CreateScope();
            scope.ServiceProvider.GetRequiredService<IUserContext>().User.AddIdentity(new ClaimsIdentity("test"));
            var descriptor = new OperationDescriptor<Empty, Empty>(typeof(IService), nameof(IService.Read));
            var plans = scope.ServiceProvider.GetRequiredService<IInvocationPlanProvider>();
            var plan = plans.Describe(descriptor, origin);
            var executor = scope.ServiceProvider.GetRequiredService<OperationExecutor>();
            Assert.IsType<AuthorizationInvocationFilter.PermissionDenied>(await executor.ExecuteInScopeAsync(descriptor, new Empty(), Context.Empty, origin));
            grants.Permissions.Add("read");
            Assert.IsType<Empty>(await executor.ExecuteInScopeAsync(descriptor, new Empty(), Context.Empty, origin));
            grants.Permissions.Clear();
            Assert.IsType<AuthorizationInvocationFilter.PermissionDenied>(await executor.ExecuteInScopeAsync(descriptor, new Empty(), Context.Empty, origin));
            Assert.Same(plan, plans.Describe(descriptor, origin));
            Assert.Equal(1, host.Services.GetRequiredService<Probe>().Constructed);
        }
        finally
        {
            await host.StopAsync();
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TenantValidation_PrecedesGrantProviderConstruction_AndProviderUsesTrustedTenant(bool rejectMismatch)
    {
        var builder = Configure();
        builder.Services.AddScoped<IPermissionChecker>(provider => new PermissionChecker(provider.GetRequiredService<IUserContext>(),
            () => provider.GetRequiredService<IPermissionGrantProvider>()));
        builder.Services.AddScoped<IPermissionGrantProvider, TenantGrants>();
        builder.Services.Configure<TenantAuthorizationOptions>(options => options.RejectMismatchedTenant = rejectMismatch);
        using var host = builder.Build();
        using var scope = host.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<IUserContext>().User.AddIdentity(new ClaimsIdentity([new Claim(ClaimTypes.TenantId, "trusted")], "test"));
        var descriptor = new OperationDescriptor<Empty, Empty>(typeof(IService), nameof(IService.Read));
        var result = await scope.ServiceProvider.GetRequiredService<OperationExecutor>().ExecuteInScopeAsync(descriptor, new Empty(),
            Context.Empty.WithTenantId("requested"), InvocationOrigin.Http);
        var probe = host.Services.GetRequiredService<Probe>();
        if (rejectMismatch)
        {
            Assert.IsType<AuthorizationInvocationFilter.PermissionDenied>(result);
            Assert.Null(probe.GrantConstructorTenant);
            Assert.Equal(0, probe.Constructed);
        }
        else
        {
            Assert.IsType<Empty>(result);
            Assert.Equal("trusted", probe.GrantConstructorTenant);
            Assert.Equal("trusted", probe.GrantQueryTenant);
        }
    }

    private static HostApplicationBuilder Configure()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.AddNOF();
        builder.Services.AddSingleton<Probe>();
        builder.Services.AddScoped<Handler>();
        builder.Services.AddScoped<IPermissionChecker, CapturingChecker>();
        builder.Services.GetOrAddSingleton<ApplicationServiceRegistry>().Add(new(typeof(IService), typeof(object))
        {
            HandlerMappings = new Dictionary<string, OperationMapping>
            {
                [nameof(IService.Read)] = new(typeof(Handler), typeof(Empty), typeof(Empty))
                { Descriptor = new OperationDescriptor<Empty, Empty>(typeof(IService), nameof(IService.Read)) }
            }
        });
        return builder;
    }

    public interface IService : IApplicationService
    {
        [RequirePermission("read")]
        Empty Read(Empty request);
    }
    public sealed class Probe
    {
        public int Constructed { get; set; }
        public string? GrantConstructorTenant { get; set; }
        public string? GrantQueryTenant { get; set; }
    }
    public sealed class DynamicGrants : IPermissionGrantProvider
    {
        public HashSet<string> Permissions { get; } = new(StringComparer.OrdinalIgnoreCase);
        public ValueTask<bool> IsGrantedAsync(ClaimsPrincipal user, string permission, Context context, CancellationToken cancellationToken)
            => ValueTask.FromResult(Permissions.Contains(permission));
    }
    public sealed class TenantGrants : IPermissionGrantProvider
    {
        private readonly Probe _probe;
        public TenantGrants(Probe probe)
        {
            _probe = probe;
            probe.GrantConstructorTenant = Context.Current.TenantId;
        }
        public ValueTask<bool> IsGrantedAsync(ClaimsPrincipal user, string permission, Context context, CancellationToken cancellationToken)
        {
            _probe.GrantQueryTenant = context.TenantId;
            return ValueTask.FromResult(true);
        }
    }
    [IgnoredHandlerPermission]
    public sealed class Handler : OperationHandler<Empty, Empty>
    {
        public Handler(Probe probe) => probe.Constructed++;
        public override Task<Empty> HandleAsync(Empty request, InvocationContext context, CancellationToken cancellationToken)
            => Task.FromResult(new Empty());
    }
    public sealed class IgnoredHandlerPermissionAttribute : Attribute, IPermissionMetadata
    {
        public IReadOnlyList<string> Permissions => ["not.a.contract.permission"];
        public new PermissionMatch Match => PermissionMatch.All;
    }
    public sealed class CapturingChecker(IUserContext user, IPermissionGrantProvider grants) : IPermissionChecker
    {
        private readonly PermissionChecker _inner = new(user, () => grants);
        public AuthorizationPolicy? Policy { get; private set; }
        public ValueTask<bool> IsGrantedAsync(string permission, CancellationToken cancellationToken = default) => _inner.IsGrantedAsync(permission, cancellationToken);
        public ValueTask<PermissionCheckResult> CheckAsync(AuthorizationPolicy policy, ClaimsPrincipal principal, Context context, CancellationToken cancellationToken = default)
        {
            Policy = policy;
            return _inner.CheckAsync(policy, principal, context, cancellationToken);
        }
    }
}
