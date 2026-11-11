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

    /// <summary>Adds one scoped application handler for a request/response pair.</summary>
    /// <typeparam name="TRequest">The request type.</typeparam>
    /// <typeparam name="TResponse">The business response type.</typeparam>
    /// <typeparam name="THandler">The concrete handler implementation.</typeparam>
    public static IServiceCollection AddRequestHandler<TRequest, TResponse,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] THandler>(this IServiceCollection services)
        where THandler : class, IRequestHandler<TRequest, TResponse>
    {
        ArgumentNullException.ThrowIfNull(services);
        if (services.Any(static descriptor => descriptor.ServiceType == typeof(IRequestHandler<TRequest, TResponse>)))
        {
            throw new InvalidOperationException($"An application handler is already registered for '{typeof(TRequest)}' and '{typeof(TResponse)}'.");
        }
        services.AddNOFRequests();
        services.AddScoped<IRequestHandler<TRequest, TResponse>, THandler>();
        return services;
    }
}
