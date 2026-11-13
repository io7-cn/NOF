using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NOF.Abstraction;
using System.Reflection;

namespace NOF.Hosting;

/// <summary>Records an assembly whose generated registrations have been applied to this service collection.</summary>
public sealed record ApplicationPartRegistration(Assembly Assembly);

public static partial class NOFHostingExtensions
{
    /// <summary>Applies an application assembly's generated registrations once to a native host builder.</summary>
    public static IHostApplicationBuilder AddApplicationPart(this IHostApplicationBuilder builder, Assembly assembly)
    {
        builder.Services.AddApplicationPart(assembly);
        return builder;
    }

    /// <summary>Applies generated registrations once; usable by native web, worker and client builders.</summary>
    public static IServiceCollection AddApplicationPart(this IServiceCollection services, Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(assembly);
        if (services.Any(descriptor => descriptor.ServiceType == typeof(ApplicationPartRegistration)
            && descriptor.ImplementationInstance is ApplicationPartRegistration part && part.Assembly == assembly))
        {
            return services;
        }
        services.AddSingleton(new ApplicationPartRegistration(assembly));
        foreach (var attribute in assembly.GetCustomAttributes<AssemblyInitializeAttribute>())
        {
            attribute.Initialize(services);
        }
        return services;
    }
}
