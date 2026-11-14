using Microsoft.Extensions.DependencyInjection;
using System.Security.Claims;
using Xunit;

namespace NOF.Abstraction.Tests;

public sealed class PermissionCheckerTests
{
    [Fact]
    public void SharedRuntime_DoesNotDependOnApplicationDomainInfrastructureOrAspNetCore()
    {
        foreach (var type in new[] { typeof(PermissionDefinition), typeof(PermissionMatch), typeof(IPermissionMetadata),
            typeof(AuthorizationPolicy), typeof(PermissionCatalog), typeof(IPermissionChecker) })
        {
            Assert.Equal(typeof(Context).Assembly, type.Assembly);
        }
        var references = typeof(PermissionChecker).Assembly.GetReferencedAssemblies().Select(assembly => assembly.Name!).ToArray();
        Assert.DoesNotContain(references, name => name is "NOF.Contract" or "NOF.Application" or "NOF.Domain" or "NOF.Infrastructure"
            || name.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal));
    }
    [Fact]
    public void Snapshots_DoNotRetainMutableCollections()
    {
        string[] ids = ["read", "write"];
        var requirement = new PermissionRequirement(ids, PermissionMatch.Any);
        ids[0] = "changed";
        var policy = new AuthorizationPolicy(true, [requirement]);
        Assert.Equal(PermissionMatch.Any, Assert.Single(policy.Requirements).Match);
        Assert.Equal("read", policy.Requirements[0].Permissions[0]);
        Assert.Throws<NotSupportedException>(() => ((IList<string>)policy.Requirements[0].Permissions)[0] = "changed");
        Assert.Throws<ArgumentException>(() => new PermissionRequirement(["read"], (PermissionMatch)42));
    }

    [Theory]
    [InlineData(PermissionMatch.All, "read", false)]
    [InlineData(PermissionMatch.All, "*", true)]
    [InlineData(PermissionMatch.Any, "read", true)]
    [InlineData(PermissionMatch.Any, "other", false)]
    public async Task Checker_UsesAnyAllAndWildcardGrants(PermissionMatch match, string grant, bool allowed)
    {
        using var provider = new ServiceCollection().AddPermissions(new PermissionDefinition("read", "Read"), new("write", "Write")).BuildServiceProvider();
        using var scope = provider.CreateScope();
        var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Permission, grant)], "test"));
        var decision = await scope.ServiceProvider.GetRequiredService<IPermissionChecker>()
            .CheckAsync(new(true, [new(["read", "write"], match)]), principal, Context.Empty);
        Assert.Equal(allowed, decision is PermissionCheckResult.Granted);
    }

    [Fact]
    public async Task Checker_RejectsAnonymousAndUnauthenticatedSupplementalClaims_AndRequiresEveryGroup()
    {
        using var provider = new ServiceCollection().AddPermissions(new PermissionDefinition("read", "Read"), new("write", "Write")).BuildServiceProvider();
        using var scope = provider.CreateScope();
        var checks = scope.ServiceProvider.GetRequiredService<IPermissionChecker>();
        var user = scope.ServiceProvider.GetRequiredService<IUserContext>();
        var both = new AuthorizationPolicy(true, [new(["read"]), new(["write"], PermissionMatch.Any)]);
        Assert.True(await checks.CheckAsync(both, user.User, Context.Empty) is PermissionCheckResult.AuthenticationRequired);
        user.User.AddIdentity(new ClaimsIdentity([new Claim(ClaimTypes.Permission, "read")], "test"));
        user.User.AddIdentity(new ClaimsIdentity([new Claim(ClaimTypes.Permission, "write")]));
        Assert.True(await checks.IsGrantedAsync("read"));
        Assert.False(await checks.IsGrantedAsync("write"));
        Assert.True(await checks.CheckAsync(both, user.User, Context.Empty) is PermissionCheckResult.Denied);
        user.Logout();
        Assert.False(await checks.IsGrantedAsync("read"));
        Assert.True(await checks.CheckAsync(AuthorizationPolicy.Anonymous, user.User, Context.Empty) is PermissionCheckResult.Granted);
    }

    [Fact]
    public async Task CustomGrantSource_ReceivesExplicitTenant_WhileDefaultSemanticsStayInFramework()
    {
        var grants = new GrantProbe();
        var services = new ServiceCollection();
        services.AddScoped<IPermissionGrantProvider>(_ => grants);
        services.AddPermissions(new PermissionDefinition("read", "Read"));
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var checks = scope.ServiceProvider.GetRequiredService<IPermissionChecker>();
        var user = new ClaimsPrincipal(new ClaimsIdentity("test"));
        var context = Context.Empty.WithTenantId("tenant-a");
        Assert.True(await checks.CheckAsync(new(true, [new(["read"])]), user, context) is PermissionCheckResult.Granted);
        Assert.Same(context, grants.Context);
        Assert.Same(user, grants.User);
        grants.Error = new InvalidOperationException("Database unavailable");
        Assert.Same(grants.Error, await Assert.ThrowsAsync<InvalidOperationException>(() => checks.CheckAsync(new(true, [new(["read"])]), user, context).AsTask()));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => checks.CheckAsync(AuthorizationPolicy.Anonymous, user, context, cancellation.Token).AsTask());
    }

    [Fact]
    public void Catalog_IsHostLocalFrozenAndStrict_ForEveryAlternative()
    {
        var first = new ServiceCollection().AddPermissions(new PermissionDefinition("read", "Read", "Configuration"));
        Assert.Throws<InvalidOperationException>(() => first.AddPermissions(new PermissionDefinition("READ", "Duplicate")));
        using var one = first.BuildServiceProvider();
        using var two = new ServiceCollection().AddPermissions().BuildServiceProvider();
        var catalog = one.GetRequiredService<PermissionCatalog>();
        Assert.Equal("Configuration", Assert.Single(catalog.GetAll()).Group);
        Assert.Empty(two.GetRequiredService<PermissionCatalog>().GetAll());
        Assert.Throws<InvalidOperationException>(() => catalog.Validate(new(true, [new(["read", "typo"], PermissionMatch.Any)])));
        Assert.Throws<InvalidOperationException>(() => catalog.Add(new("write", "Write")));
    }

    [Fact]
    public async Task GrantProvider_IsConstructedLazilyUnderTheExplicitTenant_AndAmbientStateIsRestored()
    {
        var constructed = new List<string>();
        var services = new ServiceCollection().AddPermissions(new PermissionDefinition("read", "Read"));
        services.AddScoped<IPermissionGrantProvider>(_ =>
        {
            constructed.Add(Context.Current.TenantId);
            return new GrantProbe();
        });
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var checks = scope.ServiceProvider.GetRequiredService<IPermissionChecker>();
        var user = scope.ServiceProvider.GetRequiredService<IUserContext>();
        var policy = new AuthorizationPolicy(true, [new(["read"])]);
        Assert.Empty(constructed);
        Assert.True(await checks.CheckAsync(policy, user.User, Context.Empty) is PermissionCheckResult.AuthenticationRequired);
        Assert.Empty(constructed);
        user.User.AddIdentity(new ClaimsIdentity("test"));
        using var outside = Context.PushCurrent(Context.Empty.WithTenantId("outside"));
        Assert.True(await checks.CheckAsync(policy, user.User, Context.Empty.WithTenantId("trusted")) is PermissionCheckResult.Granted);
        Assert.Equal(["trusted"], constructed);
        Assert.Equal("outside", Context.Current.TenantId);
    }

    public sealed class GrantProbe : IPermissionGrantProvider
    {
        public Context? Context { get; private set; }
        public ClaimsPrincipal? User { get; private set; }
        public Exception? Error { get; set; }
        public ValueTask<bool> IsGrantedAsync(ClaimsPrincipal user, string permission, Context context, CancellationToken cancellationToken)
        {
            User = user;
            Context = context;
            if (Error is not null)
            {
                throw Error;
            }
            return ValueTask.FromResult(true);
        }
    }
}
