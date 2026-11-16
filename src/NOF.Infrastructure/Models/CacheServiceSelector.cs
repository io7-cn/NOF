using Microsoft.Extensions.DependencyInjection;

namespace NOF.Infrastructure;

/// <summary>Selects the cache provider after common cache services and policy are registered.</summary>
public readonly record struct CacheServiceSelector(IServiceCollection Services);
