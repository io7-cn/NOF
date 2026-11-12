using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Moq;
using NOF.Abstraction;
using NOF.Application;
using NOF.Contract;
using NOF.Domain;
using NOF.Hosting;
using System.Security.Claims;
using Xunit;

namespace NOF.Infrastructure.Tests;

public sealed class ApplicationServiceInvocationAuthorizationTests
{
    [Fact]
    public async Task ServerInvocation_ShouldReturn401_WhenUserIsUnauthenticated()
    {
        await using var provider = BuildServiceProvider();
        await using var callerScope = provider.CreateAsyncScope();

        var invoker = callerScope.ServiceProvider.GetRequiredService<IApplicationServiceInvoker>();
        Result<GetFleetOverviewResponse> result = (await invoker.InvokeAsync<IProtectedFleetService>(nameof(IProtectedFleetService.GetFleetOverview), new Empty(), Context.Empty)).Require<Result<GetFleetOverviewResponse>>(Result<GetFleetOverviewResponse>.CreateFailure);
        var recorder = provider.GetRequiredService<InvocationRecorder>();

        Assert.NotNull(result.ExpectFailure());
        Assert.Equal("401", result.ExpectFailure().Code);
        Assert.Equal("Please login first", result.ExpectFailure().Message);
        Assert.Equal(0, recorder.Count);
    }

    [Fact]
    public async Task ServerInvocation_ShouldInvokeHandler_WhenUserHasPermission()
    {
        await using var provider = BuildServiceProvider();
        await using var callerScope = provider.CreateAsyncScope();
        var userContext = (UserContext)callerScope.ServiceProvider.GetRequiredService<IUserContext>();
        userContext.Logout();
        userContext.User.AddIdentity(TestPrincipalFactory.CreateAuthenticatedIdentity((ClaimTypes.Permission, "fleet.read")));

        var invoker = callerScope.ServiceProvider.GetRequiredService<IApplicationServiceInvoker>();
        Result<GetFleetOverviewResponse> result = (await invoker.InvokeAsync<IProtectedFleetService>(nameof(IProtectedFleetService.GetFleetOverview), new Empty(), Context.Empty)).Require<Result<GetFleetOverviewResponse>>(Result<GetFleetOverviewResponse>.CreateFailure);
        var recorder = provider.GetRequiredService<InvocationRecorder>();

        Assert.NotNull(result.ExpectSuccess());
        Assert.NotNull(result.ExpectSuccess());
        Assert.Equal("fleet", result.ExpectSuccess().Name);
        Assert.Equal(1, recorder.Count);
    }

    [Fact]
    public async Task ServerInvocation_ShouldPreserveExplicitContextAndIsolateIdentityChanges()
    {
        await using var provider = BuildServiceProvider();
        await using var callerScope = provider.CreateAsyncScope();
        var callerUser = callerScope.ServiceProvider.GetRequiredService<IUserContext>();
        callerUser.User.AddIdentity(TestPrincipalFactory.CreateAuthenticatedIdentity((ClaimTypes.Permission, "fleet.read")));
        var key = new object();
        var value = new object();
        var context = Context.Empty.WithTenantId("tenant-a").WithItem(key, value);
        var invoker = callerScope.ServiceProvider.GetRequiredService<IApplicationServiceInvoker>();

        var execution = await invoker.InvokeAsync<IProtectedFleetService>(nameof(IProtectedFleetService.InspectContext),
            new ContextCheckRequest(key, value), context);

        Assert.IsType<Success>(execution.Require<Result>(Result.CreateFailure).Value);
        Assert.True(callerUser.User.IsAuthenticated);
        Assert.Contains(callerUser.User.Claims, static claim => claim.Type == ClaimTypes.Permission && claim.Value == "fleet.read");
        Assert.Equal("tenant-a", context.TenantId);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task LocalInvocation_ShouldUseScopedIdentityInsteadOfAuthenticatingContextHeaders(bool authenticated)
    {
        var authorityScopes = new Mock<IServiceScopeFactory>(MockBehavior.Strict);
        using var cache = new ResourceServerJwksCacheService(authorityScopes.Object,
            Options.Create(new AuthenticationResourceServerOptions()));
        await using var provider = BuildServiceProvider(cache);
        await using var callerScope = provider.CreateAsyncScope();
        if (authenticated)
        {
            callerScope.ServiceProvider.GetRequiredService<IUserContext>().User.AddIdentity(
                TestPrincipalFactory.CreateAuthenticatedIdentity((ClaimTypes.Permission, "fleet.read")));
        }
        var execution = await callerScope.ServiceProvider.GetRequiredService<IApplicationServiceInvoker>()
            .InvokeAsync<IProtectedFleetService>(nameof(IProtectedFleetService.GetFleetOverview), new Empty(),
                Context.Empty.WithItem(NOFAbstractionConstants.Transport.Headers.Authorization, "Bearer untrusted"));
        if (authenticated)
        {
            Assert.IsType<GetFleetOverviewResponse>(execution.Require<Result<GetFleetOverviewResponse>>().Value);
        }
        else
        {
            Assert.Equal("401", Assert.IsType<ServiceRejected>(execution.Value).Failure.Code);
        }
        authorityScopes.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ServerInvocation_ShouldCreateAndDisposeAnIsolatedScopeForEachInvocation()
    {
        await using var provider = BuildServiceProvider();
        await using var callerScope = provider.CreateAsyncScope();

        var dbContext = callerScope.ServiceProvider.GetRequiredService<IDbContext>();
        var marker = callerScope.ServiceProvider.GetRequiredService<LocalScopeMarker>();
        var invoker = callerScope.ServiceProvider.GetRequiredService<IApplicationServiceInvoker>();

        var firstResult = (await invoker.InvokeAsync<IProtectedFleetService>(nameof(IProtectedFleetService.CheckScope), new ScopeCheckRequest(dbContext, marker), Context.Empty)).Require<Result>(Result.CreateFailure);
        var secondResult = (await invoker.InvokeAsync<IProtectedFleetService>(nameof(IProtectedFleetService.CheckScope), new ScopeCheckRequest(dbContext, marker), Context.Empty)).Require<Result>(Result.CreateFailure);
        var probe = callerScope.ServiceProvider.GetRequiredService<LocalScopeProbe>();

        Assert.NotNull(firstResult.ExpectSuccess());
        Assert.NotNull(secondResult.ExpectSuccess());
        Assert.False(probe.HandlerUsedCallerScopeDbContext);
        Assert.False(probe.HandlerUsedCallerScopeMarker);
        Assert.True(probe.EventHandlerUsedHandlerDbContext);
        Assert.True(probe.EventHandlerUsedHandlerScopeMarker);
        Assert.True(probe.EventHandlerUsedHandlerContext);
        Assert.Equal(2, probe.HandlerScopeMarkers.Count);
        Assert.All(probe.HandlerScopeMarkers, handlerMarker => Assert.NotSame(marker, handlerMarker));
        Assert.NotSame(probe.HandlerScopeMarkers[0], probe.HandlerScopeMarkers[1]);
        Assert.Equal(2, probe.DaemonServiceActivations);
        Assert.Equal(2, probe.DaemonServiceAsyncDisposals);
    }

    private static ServiceProvider BuildServiceProvider(ResourceServerJwksCacheService? authenticationCache = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();

        services.AddScoped<IUserContext, UserContext>();
        services.AddScoped<IDbContext, LocalScopedDbContext>();
        services.AddScoped<LocalScopeMarker>();
        services.AddSingleton<InvocationRecorder>();
        services.AddSingleton<LocalScopeProbe>();
        services.AddScoped<ProtectedFleetServer>();
        services.AddTransient<GetFleetOverviewHandler>();
        services.AddTransient<ScopeCheckHandler>();
        services.AddTransient<ScopeCheckEventHandler>();
        services.AddScoped<ContextCheckHandler>();
        services.AddScoped<IApplicationServiceInvoker, ApplicationServiceInvocation>();
        services.AddScoped<IInboundAuthorizationHandler, DefaultInboundAuthorizationHandler>();
        services.AddTransient<AuthorizationInboundMiddleware>();
        services.AddRequestInboundMiddleware<AuthorizationInboundMiddleware>();
        if (authenticationCache is not null)
        {
            services.AddSingleton(authenticationCache);
            services.AddRequestInboundMiddleware<AuthenticationResourceServerInboundMiddleware>();
        }
        services.AddScoped<IEventPublisher, InMemoryEventPublisher>();
        services.AddScoped<IDaemonService, EventPublisherAmbientDaemonService>();
        services.AddScoped<IDaemonService, ScopeLifecycleDaemonService>();

        services.AddScoped<RequestInboundPipelineExecutor>();
        services.AddScoped<ApplicationServiceInvocationResolver>();

        var rpcServerRegistry = new ApplicationServiceRegistry();
        rpcServerRegistry.Add(new ApplicationServiceRegistration(typeof(IProtectedFleetService), typeof(ProtectedFleetServer)));
        services.AddSingleton(rpcServerRegistry);
        services.AddSingleton(_ =>
        {
            var registry = new EventHandlerRegistry();
            registry.Add(new EventHandlerRegistration(typeof(ScopeCheckEventHandler), typeof(ScopeCheckEvent)));
            return registry;
        });

        return services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true
        });
    }
}

public partial interface IProtectedFleetService : IApplicationService
{
    [RequirePermission("fleet.read")]
    Result<GetFleetOverviewResponse> GetFleetOverview(Empty request);

    Result CheckScope(ScopeCheckRequest request);

    [RequirePermission("fleet.read")]
    Result InspectContext(ContextCheckRequest request);
}

public sealed record ContextCheckRequest(object Key, object Value);

public sealed class ContextCheckHandler(IUserContext user) : ServiceOperation<ContextCheckRequest, Result>
{
    public override Task<Result> HandleAsync(ContextCheckRequest request, Context context, CancellationToken cancellationToken)
    {
        Assert.Same(request.Value, context[request.Key]);
        Assert.Same(context, Context.Current);
        Assert.Equal("tenant-a", context.TenantId);
        Assert.True(user.User.IsAuthenticated);
        user.Logout();
        return Task.FromResult(Result.Success());
    }
}

public sealed record GetFleetOverviewResponse(string Name);

public sealed record ScopeCheckRequest(IDbContext ExpectedDbContext, LocalScopeMarker ExpectedMarker);

public sealed record ScopeCheckEvent(IDbContext HandlerDbContext, LocalScopeMarker HandlerMarker);

public sealed class LocalScopeMarker;

public sealed class LocalScopeProbe
{
    public bool HandlerUsedCallerScopeDbContext { get; set; }

    public bool HandlerUsedCallerScopeMarker { get; set; }

    public bool EventHandlerUsedHandlerDbContext { get; set; }

    public bool EventHandlerUsedHandlerScopeMarker { get; set; }

    public Context? HandlerContext { get; set; }

    public bool EventHandlerUsedHandlerContext { get; set; }

    public List<LocalScopeMarker> HandlerScopeMarkers { get; } = [];

    public int DaemonServiceActivations { get; set; }

    public int DaemonServiceAsyncDisposals { get; set; }

}

public sealed class InvocationRecorder
{
    public int Count { get; set; }
}

public sealed class ProtectedFleetServer : ApplicationService<IProtectedFleetService>
{
    private static readonly IReadOnlyDictionary<string, ServiceOperationMapping> _mappings =
        new Dictionary<string, ServiceOperationMapping>
        {
            [nameof(IProtectedFleetService.GetFleetOverview)] =
                new(typeof(GetFleetOverviewHandler), typeof(Empty), typeof(Result<GetFleetOverviewResponse>)),
            [nameof(IProtectedFleetService.CheckScope)] =
                new(typeof(ScopeCheckHandler), typeof(ScopeCheckRequest), typeof(Result)),
            [nameof(IProtectedFleetService.InspectContext)] =
                new(typeof(ContextCheckHandler), typeof(ContextCheckRequest), typeof(Result))
        };

    protected override IReadOnlyDictionary<string, ServiceOperationMapping> GetHandlerMappings() => _mappings;
}

public sealed class ScopeLifecycleDaemonService : IDaemonService, IAsyncDisposable
{
    private readonly LocalScopeProbe _probe;

    public ScopeLifecycleDaemonService(LocalScopeProbe probe)
    {
        _probe = probe;
        _probe.DaemonServiceActivations++;
    }

    public ValueTask DisposeAsync()
    {
        _probe.DaemonServiceAsyncDisposals++;
        return ValueTask.CompletedTask;
    }
}

public sealed class ScopeCheckHandler(
    IDbContext dbContext,
    LocalScopeMarker marker,
    LocalScopeProbe probe) : ServiceOperation<ScopeCheckRequest, Result>
{
    public override Task<Result> HandleAsync(ScopeCheckRequest request, Context context, CancellationToken cancellationToken)
    {
        probe.HandlerUsedCallerScopeDbContext = ReferenceEquals(request.ExpectedDbContext, dbContext);
        probe.HandlerUsedCallerScopeMarker = ReferenceEquals(request.ExpectedMarker, marker);
        probe.HandlerScopeMarkers.Add(marker);
        probe.HandlerContext = context;
        new ScopeCheckEvent(dbContext, marker).PublishAsEvent();
        return Task.FromResult(Result.Success());
    }
}

public sealed class ScopeCheckEventHandler(
    IDbContext dbContext,
    LocalScopeMarker marker,
    LocalScopeProbe probe) : InMemoryEventHandler<ScopeCheckEvent>
{
    public override Task HandleAsync(ScopeCheckEvent @event, Context context, CancellationToken cancellationToken)
    {
        probe.EventHandlerUsedHandlerDbContext = ReferenceEquals(@event.HandlerDbContext, dbContext);
        probe.EventHandlerUsedHandlerScopeMarker = ReferenceEquals(@event.HandlerMarker, marker);
        probe.EventHandlerUsedHandlerContext = ReferenceEquals(probe.HandlerContext, context);
        return Task.CompletedTask;
    }
}

public sealed class LocalScopedDbContext : IDbContext
{
    public IRepository<TEntity> Set<TEntity>()
        where TEntity : class
        => throw new NotSupportedException();

    public int SaveChanges()
        => 0;

    public int SaveChanges(bool acceptAllChangesOnSuccess)
        => 0;

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(0);

    public Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
        => Task.FromResult(0);

    public IDbContextTransaction BeginTransaction()
        => throw new NotSupportedException();

    public Task<IDbContextTransaction> BeginTransactionAsync(CancellationToken cancellationToken = default)
        => throw new NotSupportedException();
}

public sealed class GetFleetOverviewHandler(InvocationRecorder recorder) : ServiceOperation<Empty, Result<GetFleetOverviewResponse>>
{
    public override Task<Result<GetFleetOverviewResponse>> HandleAsync(Empty request, Context context, CancellationToken cancellationToken)
    {
        recorder.Count++;
        return Task.FromResult(Result.Success(new GetFleetOverviewResponse("fleet")));
    }
}

public static class TestPrincipalFactory
{
    public static ClaimsIdentity CreateAuthenticatedIdentity(params (string Type, string Value)[] claims)
    {
        var identity = new ClaimsIdentity("Test");
        identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, "user-1"));

        foreach (var (type, value) in claims)
        {
            identity.AddClaim(new Claim(type, value));
        }

        return identity;
    }
}
