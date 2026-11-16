using NOF.Infrastructure;
using System.Diagnostics.CodeAnalysis;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>Declares host-owned filter application rules without selecting an engine.</summary>
public static class InvocationServiceCollectionExtensions
{
    /// <summary>Applies a filter globally; use AddInvocationFilters for finer scopes.</summary>
    public static IServiceCollection AddInvocationFilter<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TFilter>(
        this IServiceCollection services, int order = 0) where TFilter : class, IInvocationFilter
        => services.AddInvocationFilters(filters => filters.Global.Use<TFilter>(order));

    /// <summary>Configures Global, Service and UseCase rules and captures static catalogs.</summary>
    public static IServiceCollection AddInvocationFilters(this IServiceCollection services, Action<InvocationFiltersBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        configure(new(services, services.GetOrAddSingleton<InvocationFilterRegistry>()));
        return services;
    }
}
