using Microsoft.Extensions.DependencyInjection;

namespace NOF.Infrastructure;

/// <summary>Selects the cache provider after common cache services and policy are registered.</summary>
public readonly record struct CacheServiceSelector(IServiceCollection Services);
/// <summary>Selects the object storage provider after common tenant-prefix policy is registered.</summary>
public readonly record struct ObjectStorageSelector(IServiceCollection Services);
/// <summary>Selects a transient broadcast provider.</summary>
public readonly record struct BackplaneSelector(IServiceCollection Services);
/// <summary>Selects the host's ID generation policy.</summary>
public readonly record struct IdGeneratorSelector(IServiceCollection Services);
