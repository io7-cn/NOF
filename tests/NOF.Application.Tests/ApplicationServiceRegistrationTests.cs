using Microsoft.Extensions.DependencyInjection;
using NOF.Contract;
using Xunit;

namespace NOF.Application.Tests;

public class ApplicationServiceRegistrationTests
{
    [Fact]
    public void RegistrationIsIdempotentAndServerLifetimeIsScoped()
    {
        var services = new ServiceCollection();
        Assert.Same(services, services.AddApplicationService<FirstServer>());
        services.AddApplicationService<FirstServer>();
        var descriptor = Assert.Single(services, item => item.ServiceType == typeof(FirstServer));
        Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
        Assert.Single(services, item => item.ServiceType == typeof(ApplicationServiceRegistry));

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        var registry = provider.GetRequiredService<ApplicationServiceRegistry>();
        Assert.True(registry.TryGetImplementationType(typeof(IRegistrationService), out var implementation));
        Assert.Equal(typeof(FirstServer), implementation);
        using var first = provider.CreateScope();
        using var second = provider.CreateScope();
        Assert.Same(first.ServiceProvider.GetRequiredService<FirstServer>(), first.ServiceProvider.GetRequiredService<FirstServer>());
        Assert.NotSame(first.ServiceProvider.GetRequiredService<FirstServer>(), second.ServiceProvider.GetRequiredService<FirstServer>());
    }

    [Fact]
    public void LastRegistrationWinsWithoutChangingOtherServiceCollections()
    {
        var first = new ServiceCollection();
        var second = new ServiceCollection();
        first.AddApplicationService<FirstServer>();
        first.AddApplicationService<ReplacementServer>();
        second.AddApplicationService<FirstServer>();
        using var firstProvider = first.BuildServiceProvider();
        using var secondProvider = second.BuildServiceProvider();
        Assert.True(firstProvider.GetRequiredService<ApplicationServiceRegistry>().TryGetImplementationType(typeof(IRegistrationService), out var replacement));
        Assert.Equal(typeof(ReplacementServer), replacement);
        Assert.True(secondProvider.GetRequiredService<ApplicationServiceRegistry>().TryGetImplementationType(typeof(IRegistrationService), out var original));
        Assert.Equal(typeof(FirstServer), original);
    }

    [Fact]
    public void ApplicationDefaultsPreserveAnExistingServerRegistration()
    {
        var services = new ServiceCollection();
        services.AddApplicationService<FirstServer>();
        services.AddNOFApplication();
        using var provider = services.BuildServiceProvider();
        Assert.True(provider.GetRequiredService<ApplicationServiceRegistry>().TryGetImplementationType(typeof(IRegistrationService), out var implementation));
        Assert.Equal(typeof(FirstServer), implementation);
    }

    public interface IRegistrationService : IApplicationService;

    public class FirstServer : ApplicationService<IRegistrationService>, IApplicationServiceImplementation
    {
        public static IReadOnlyDictionary<string, ServiceOperationMapping> HandlerMappings { get; } = new Dictionary<string, ServiceOperationMapping>();
        protected override IReadOnlyDictionary<string, ServiceOperationMapping> GetHandlerMappings() => HandlerMappings;
    }

    public class ReplacementServer : ApplicationService<IRegistrationService>, IApplicationServiceImplementation
    {
        public static IReadOnlyDictionary<string, ServiceOperationMapping> HandlerMappings { get; } = new Dictionary<string, ServiceOperationMapping>();
        protected override IReadOnlyDictionary<string, ServiceOperationMapping> GetHandlerMappings() => HandlerMappings;
    }
}
