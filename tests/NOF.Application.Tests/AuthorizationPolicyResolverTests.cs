using NOF.Abstraction;
using NOF.Contract;
using Xunit;

namespace NOF.Application.Tests;

public sealed class AuthorizationPolicyResolverTests
{
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
    public void Resolver_RecognizesTypedMetadata_AndRejectsImplementationSources()
    {
        var custom = AuthorizationPolicyResolver.Resolve(typeof(ICustom));
        Assert.Equal("read", Assert.Single(Assert.Single(custom.Requirements).Permissions));
        Assert.Throws<ArgumentException>(() => AuthorizationPolicyResolver.Resolve(typeof(AuthorizationPolicyResolverTests)));
        Assert.Throws<ArgumentException>(() => new RequirePermissionAttribute());
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
}
