using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using NOF.Abstraction;
using NOF.Hosting;
using Xunit;

[assembly: AssemblyInitialize<NOF.Hosting.AspNetCore.Tests.TypeResolverSynchronizationAssemblyInitializer>]

namespace NOF.Hosting.AspNetCore.Tests;

public sealed class TypeResolverSynchronizationTests
{
    [Fact]
    public void Create_ShouldRunAssemblyInitializerThatRegistersGeneratedServicesIntoServiceCollection()
    {
        var builder = WebApplication.CreateBuilder([]).AddNOF();
        builder.AddApplicationPart(typeof(TypeResolverSynchronizationProbe).Assembly);

        Assert.Contains(
            builder.Services,
            descriptor => descriptor.ServiceType == typeof(TypeResolverSynchronizationProbe)
                && descriptor.ImplementationType == typeof(TypeResolverSynchronizationProbe));
    }
}

public sealed class TypeResolverSynchronizationAssemblyInitializer : IAssemblyInitializer
{
    public static void Initialize(IServiceCollection services)
    {
        services.AddSingleton<TypeResolverSynchronizationProbe>();
    }
}

public sealed class TypeResolverSynchronizationProbe;
