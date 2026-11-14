using Microsoft.Extensions.DependencyInjection;
using NOF.Abstraction;
using NOF.Contract;
using System.Security.Claims;
using Xunit;

namespace NOF.Authorization.Tests;

public sealed class AuthorizationTests
{
    [Fact]
    public void SharedRuntime_DoesNotDependOnApplicationDomainInfrastructureOrAspNetCore()
    {
        var references = typeof(PermissionChecker).Assembly.GetReferencedAssemblies().Select(assembly => assembly.Name!).ToArray();
        Assert.DoesNotContain(references, name => name is "NOF.Application" or "NOF.Domain" or "NOF.Infrastructure"
            || name.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal));
    }
    [Fact]
    public void ContractMethods_OverrideInterfaceDefaults_AndKeepDeclarationSources()
    {
        var policy = AuthorizationPolicyResolver.Resolve(typeof(IContract), typeof(IContract).GetMethod(nameof(IContract.Read)));
        Assert.True(policy.RequiresAuthentication);
        Assert.Equal("read", Assert.Single(Assert.Single(policy.Requirements).Permissions));
        Assert.Single(policy.Sources);
        var anonymousContract = AuthorizationPolicyResolver.Resolve(typeof(IContract), typeof(IContract).GetMethod(nameof(IContract.Public)));
        Assert.False(anonymousContract.RequiresAuthentication);
        Assert.Empty(anonymousContract.Requirements);
        var login = AuthorizationPolicyResolver.Resolve(typeof(IContract), typeof(IContract).GetMethod(nameof(IContract.Login)));
        Assert.True(login.RequiresAuthentication);
        Assert.Empty(login.Requirements);
    }

    [Fact]
    public void InheritedInterfaceConflicts_AreRejected_WhileIdenticalDiamondDefaultsAgree()
    {
        Assert.Throws<InvalidOperationException>(() => AuthorizationPolicyResolver.Resolve(typeof(IAmbiguous)));
        Assert.Throws<InvalidOperationException>(() => AuthorizationPolicyResolver.Resolve(typeof(IUnevenBranches)));
        var shared = AuthorizationPolicyResolver.Resolve(typeof(IDiamond));
        Assert.Equal("read", Assert.Single(Assert.Single(shared.Requirements).Permissions));
        var overridden = AuthorizationPolicyResolver.Resolve(typeof(IOverride));
        Assert.False(overridden.RequiresAuthentication);
    }

    [Fact]
    public void InheritedOperationDeclarations_RejectAmbiguity_AndExplicitMethodCanResolveIt()
    {
        var operation = typeof(IRead).GetMethod(nameof(IRead.Execute))!;
        Assert.Throws<InvalidOperationException>(() => AuthorizationPolicyResolver.Resolve(typeof(IConflictingOperations), operation));
        var policy = AuthorizationPolicyResolver.Resolve(typeof(IResolvedOperation), typeof(IResolvedOperation).GetMethod(nameof(IResolvedOperation.Execute)));
        Assert.False(policy.RequiresAuthentication);
    }

    [Fact]
    public void Snapshots_RecognizeSemanticInterfaces_RejectConflicts_AndDoNotRetainMutableCollections()
    {
        string[] ids = ["read", "write"];
        var declaration = new RequirePermissionAttribute(ids) { Match = PermissionMatch.Any };
        ids[0] = "changed";
        var policy = new AuthorizationPolicy(true, [new(declaration.Permissions, declaration.Match)]);
        declaration.Match = PermissionMatch.All;
        Assert.Equal(PermissionMatch.Any, Assert.Single(policy.Requirements).Match);
        Assert.Equal("read", policy.Requirements[0].Permissions[0]);
        Assert.Throws<NotSupportedException>(() => ((IList<string>)policy.Requirements[0].Permissions)[0] = "changed");
        var custom = AuthorizationPolicyResolver.Resolve(typeof(ICustom));
        Assert.Equal("read", Assert.Single(Assert.Single(custom.Requirements).Permissions));
        Assert.Throws<ArgumentException>(() => AuthorizationPolicyResolver.Resolve(typeof(AuthorizationTests)));
        Assert.Throws<ArgumentException>(() => new RequirePermissionAttribute());
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

    [RequirePermission("write")]
    public interface IContract
    {
        [RequirePermission("read")]
        void Read();
        [AllowAnonymous]
        void Public();
        [RequireAuthenticated]
        void Login();
    }
    [RequirePermission("read")]
    public interface ILeft;
    [RequirePermission("write")]
    public interface IRight;
    public interface IAmbiguous : ILeft, IRight;
    public interface ILongBranch : IRight;
    public interface IUnevenBranches : ILeft, ILongBranch;
    public interface IFirst : ILeft;
    public interface ISecond : ILeft;
    public interface IDiamond : IFirst, ISecond;
    [AllowAnonymous]
    public interface IOverride : ILeft, IRight;
    public interface IRead { [RequirePermission("read")] void Execute(); }
    public interface IWrite { [RequirePermission("write")] void Execute(); }
    public interface IConflictingOperations : IRead, IWrite;
    public interface IResolvedOperation : IRead, IWrite { [AllowAnonymous] new void Execute(); }
    [CustomPermission]
    public interface ICustom;
    public sealed class CustomPermissionAttribute : Attribute, IPermissionMetadata
    {
        public IReadOnlyList<string> Permissions => ["read"];
        public new PermissionMatch Match => PermissionMatch.All;
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
