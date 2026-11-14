using Microsoft.Extensions.DependencyInjection;
using NOF.Application;
using NOF.Infrastructure;
using NOF.Infrastructure.StackExchangeRedis;
using StackExchange.Redis;

namespace NOF.Hosting;

public static partial class NOFInfrastructureExtensions
{
    extension(BackplaneSelector selector)
    {
        public BackplaneSelector AddRedisBackplane(ConfigurationOptions connectionOptions)
        {
            var services = selector.Services;
            ArgumentNullException.ThrowIfNull(connectionOptions);

            services.ReplaceOrAddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(connectionOptions));
            services.AddNOFJson();
            services.ReplaceOrAddSingleton<IBackplane, RedisBackplane>();
            return selector;
        }

        public BackplaneSelector AddRedisBackplane(string configuration, Action<ConfigurationOptions>? configureConnectionOptions = null)
        {
            var services = selector.Services;
            if (configureConnectionOptions is not null)
            {
                services.ReplaceOrAddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(configuration, configureConnectionOptions));
            }
            else
            {
                services.ReplaceOrAddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(configuration));
            }

            services.AddNOFJson();
            services.ReplaceOrAddSingleton<IBackplane, RedisBackplane>();
            return selector;
        }

    }

    extension(CacheServiceSelector selector)
    {
        public CacheServiceSelector AddRedisCache(ConfigurationOptions connectionOptions, Action<CacheServiceOptions>? configureCacheOptions = null)
        {
            var services = selector.Services;
            ArgumentNullException.ThrowIfNull(connectionOptions);

            if (configureCacheOptions is not null)
            {
                services.Configure(configureCacheOptions);
            }

            services.ReplaceOrAddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(connectionOptions));
            services.ReplaceOrAddScoped<ICacheServiceRider, RedisCacheServiceRider>();
            return selector;
        }

        public CacheServiceSelector AddRedisCache(string configuration, Action<ConfigurationOptions>? configureConnectionOptions = null, Action<CacheServiceOptions>? configureCacheOptions = null)
        {
            var services = selector.Services;
            if (configureCacheOptions is not null)
            {
                services.Configure(configureCacheOptions);
            }

            if (configureConnectionOptions is not null)
            {
                services.ReplaceOrAddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(configuration, configureConnectionOptions));
            }
            else
            {
                services.ReplaceOrAddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(configuration));
            }

            services.ReplaceOrAddScoped<ICacheServiceRider, RedisCacheServiceRider>();
            return selector;
        }
    }
}
