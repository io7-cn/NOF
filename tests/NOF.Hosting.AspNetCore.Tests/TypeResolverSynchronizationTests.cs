using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using NOF.Abstraction;
using NOF.Hosting;
using Xunit;

[assembly: AssemblyInitialize<NOF.Hosting.AspNetCore.Tests.__TypeResolverSynchronizationAssemblyInitializer>]

namespace NOF.Hosting.AspNetCore.Tests;

public sealed class TypeResolverSynchronizationTests
{
    [Fact]
    public void Create_ShouldRunAssemblyInitializerThatRegistersGeneratedServicesIntoServiceCollection()
    {
        var builder = WebApplication.CreateBuilder([]).AddNOF();
        builder.AddApplicationPart(typeof(__TypeResolverSynchronizationProbe).Assembly);

        Assert.Contains(
            builder.Services,
            descriptor => descriptor.ServiceType == typeof(__TypeResolverSynchronizationProbe)
                && descriptor.ImplementationType == typeof(__TypeResolverSynchronizationProbe));
    }
}

public sealed class __TypeResolverSynchronizationAssemblyInitializer : IAssemblyInitializer
{
    public static void Initialize(Microsoft.Extensions.DependencyInjection.IServiceCollection services)
    {
        services.AddSingleton<__TypeResolverSynchronizationProbe>();
    }
}

public sealed class __TypeResolverSynchronizationProbe;
