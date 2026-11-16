using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace NOF.UI.Tests.Extensions;

public class ServiceCollectionExtensionsTests
{
    [Fact]
    public void UiAuthorization_ReferencesOnlyFoundation()
    {
        Assert.Equal(["NOF.Abstraction"], typeof(global::NOF.UI.Components.AuthGuard).Assembly.GetReferencedAssemblies()
            .Where(assembly => assembly.Name!.StartsWith("NOF.", StringComparison.Ordinal)).Select(assembly => assembly.Name));
    }
    [Fact]
    public void AddNOFUI_ShouldRegisterPackageDefaults()
    {
        var services = new ServiceCollection();

        services.AddNOFUI();

        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(IBrowserInfoService)
            && descriptor.ImplementationType == typeof(BrowserInfoService)
            && descriptor.Lifetime == ServiceLifetime.Scoped);
        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(ILocalStorage)
            && descriptor.ImplementationType == typeof(LocalStorage)
            && descriptor.Lifetime == ServiceLifetime.Scoped);
        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(ISessionStorage)
            && descriptor.ImplementationType == typeof(SessionStorage)
            && descriptor.Lifetime == ServiceLifetime.Scoped);
    }

    [Fact]
    public void AddNOFUI_ShouldBeIdempotent()
    {
        var services = new ServiceCollection();

        services.AddNOFUI();
        services.AddNOFUI();

        _ = Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IBrowserInfoService));
        _ = Assert.Single(services, descriptor => descriptor.ServiceType == typeof(ILocalStorage));
        _ = Assert.Single(services, descriptor => descriptor.ServiceType == typeof(ISessionStorage));
    }
}
