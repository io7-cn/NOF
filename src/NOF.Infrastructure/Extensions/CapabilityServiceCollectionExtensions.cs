using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using NOF.Application;
using NOF.Domain;
using NOF.Hosting;

namespace NOF.Infrastructure;

/// <summary>Assembles optional infrastructure through explicit host choices.</summary>
public static class CapabilityServiceCollectionExtensions
{
    /// <summary>Registers the cache facade and common policy; a provider supplies the rider.</summary>
    public static CacheServiceSelector AddCacheService(this IServiceCollection services, Action<CacheServiceOptions>? configure = null)
    {
        services.AddNOFJson();
        services.AddOptions<CacheServiceOptions>();
        services.TryAddSingleton<ICacheLockRetryStrategy, ExponentialBackoffCacheLockRetryStrategy>();
        services.TryAddSingleton<CacheServiceLocalLockState>();
        services.TryAddScoped<ICacheService, CacheService>();
        services.TryAddScoped<IDistributedCache>(sp => sp.GetRequiredService<ICacheService>());
        if (configure is not null)
        {
            services.Configure(configure);
        }
        return new(services);
    }

    /// <summary>Registers tenant-prefixed object storage; the host selects a provider.</summary>
    public static ObjectStorageSelector AddObjectStorage(this IServiceCollection services, Action<ObjectStorageOptions>? configure = null)
    {
        services.AddOptions<ObjectStorageOptions>();
        services.TryAddScoped<IObjectStorage, ObjectStorageService>();
        if (configure is not null)
        {
            services.Configure(configure);
        }
        return new(services);
    }

    /// <summary>Starts explicit backplane provider selection.</summary>
    public static BackplaneSelector AddBackplane(this IServiceCollection services) => new(services);

    /// <summary>Starts explicit ID generator selection.</summary>
    public static IdGeneratorSelector AddIdGenerator(this IServiceCollection services) => new(services);

    /// <summary>Selects host-local memory caching.</summary>
    public static CacheServiceSelector AddMemoryCacheService(this CacheServiceSelector selector, Action<CacheServiceOptions>? configure = null)
    {
        var services = selector.Services;
        services.TryAddSingleton<MemoryCacheServiceRiderState>();
        services.ReplaceOrAddScoped<ICacheServiceRider, MemoryCacheServiceRider>();
        if (configure is not null)
        {
            services.Configure(configure);
        }
        return selector;
    }

    /// <summary>Selects host-local transient broadcast.</summary>
    public static BackplaneSelector AddMemoryBackplane(this BackplaneSelector selector)
    {
        var services = selector.Services;
        services.TryAddSingleton<MemoryBackplaneState>();
        services.ReplaceOrAddSingleton<IBackplane, MemoryBackplane>();
        return selector;
    }

    /// <summary>Selects Snowflake IDs using the host's deployment identity and validates its bit allocation.</summary>
    public static IdGeneratorSelector AddSnowflakeIdGenerator(this IdGeneratorSelector selector, Action<SnowflakeIdGeneratorOptions>? configure = null)
    {
        var services = selector.Services;
        services.AddOptions<SnowflakeIdGeneratorOptions>()
            .Validate(options => options.ApplicationIdBits > 0 && options.InstanceIdBits > 0 && options.SequenceBits > 0
                && options.ApplicationIdBits + options.InstanceIdBits + options.SequenceBits <= 22, "Invalid Snowflake bit allocation.");
        if (configure is not null)
        {
            services.Configure(configure);
        }
        services.TryAddSingleton<IIdGenerator>(sp => new SnowflakeIdGenerator(
            sp.GetRequiredService<IOptions<SnowflakeIdGeneratorOptions>>().Value,
            sp.GetRequiredService<IOptions<DeploymentOptions>>().Value.ServiceId,
            sp.GetRequiredService<IOptions<DeploymentOptions>>().Value.InstanceId));
        return selector;
    }
}
