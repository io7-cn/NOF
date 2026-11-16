using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
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

            services.Replace(ServiceDescriptor.Singleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(connectionOptions)));
            services.AddNOFJson();
            services.Replace(ServiceDescriptor.Singleton<IBackplane, RedisBackplane>());
            return selector;
        }

        public BackplaneSelector AddRedisBackplane(string configuration, Action<ConfigurationOptions>? configureConnectionOptions = null)
        {
            var services = selector.Services;
            if (configureConnectionOptions is not null)
            {
                services.Replace(ServiceDescriptor.Singleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(configuration, configureConnectionOptions)));
            }
            else
            {
                services.Replace(ServiceDescriptor.Singleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(configuration)));
            }

            services.AddNOFJson();
            services.Replace(ServiceDescriptor.Singleton<IBackplane, RedisBackplane>());
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

            services.Replace(ServiceDescriptor.Singleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(connectionOptions)));
            services.Replace(ServiceDescriptor.Scoped<ICacheServiceRider, RedisCacheServiceRider>());
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
                services.Replace(ServiceDescriptor.Singleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(configuration, configureConnectionOptions)));
            }
            else
            {
                services.Replace(ServiceDescriptor.Singleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(configuration)));
            }

            services.Replace(ServiceDescriptor.Scoped<ICacheServiceRider, RedisCacheServiceRider>());
            return selector;
        }
    }
}
