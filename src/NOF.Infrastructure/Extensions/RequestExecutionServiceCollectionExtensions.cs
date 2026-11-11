using Microsoft.Extensions.DependencyInjection.Extensions;
using NOF.Application;
using NOF.Infrastructure;
using System.Diagnostics.CodeAnalysis;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Registers the application executor without installing a web host, database, broker or RPC service.</summary>
public static class RequestExecutionServiceCollectionExtensions
{
    /// <summary>Adds the scoped, transport-independent application executor.</summary>
    public static IServiceCollection AddNOFRequests(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddScoped<IRequestExecutor, RequestExecutor>();
        return services;
    }

    /// <summary>Adds a selected scoped application handler for explicit composition. Use cases normally register through application parts.</summary>
    /// <typeparam name="TRequest">The request type.</typeparam>
    /// <typeparam name="TResponse">The business response type.</typeparam>
    /// <typeparam name="THandler">The concrete handler implementation.</typeparam>
    public static IServiceCollection AddRequestHandler<TRequest, TResponse,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] THandler>(this IServiceCollection services)
        where THandler : class, IRequestHandler<TRequest, TResponse>
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddNOFRequests();
        services.TryAddScoped<THandler>();
        return services;
    }
}
