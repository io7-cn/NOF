using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NOF.Application;
using NOF.Hosting;
using NOF.Infrastructure.StackExchangeRedis;
using StackExchange.Redis;
using Xunit;

namespace NOF.Infrastructure.Tests.Caching;

public sealed class RedisServiceCollectionExtensionsTests
{
    [Fact]
    public void AddRedisBackplane_OnServices_ShouldRegisterRedisBackplane()
    {
        var services = new ServiceCollection();

        services.AddBackplane().AddRedisBackplane(new ConfigurationOptions
        {
            EndPoints = { "localhost:6379" },
            AbortOnConnectFail = false
        });

        services.AddSingleton(new Moq.Mock<IConnectionMultiplexer>().Object);
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true });
        Assert.IsType<RedisBackplane>(provider.GetRequiredService<IBackplane>());
        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IConnectionMultiplexer));
        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(IBackplane) &&
            descriptor.ImplementationType == typeof(RedisBackplane));
    }

    [Fact]
    public void AddRedisCache_OnServices_ShouldRegisterRedisCacheServiceRider()
    {
        var services = new ServiceCollection();

        services.AddCacheService()
            .Configure(options => options.KeyPrefix = "app:{tenantId}:")
            .AddRedisCache(
            new ConfigurationOptions
            {
                EndPoints = { "localhost:6379" },
                AbortOnConnectFail = false
            },
            options => options.MinimumLockRenewalDuration = TimeSpan.FromMinutes(3))
            .Configure(options => options.LockRenewalIntervalFactor = 0.75);

        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IConnectionMultiplexer));
        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(ICacheServiceRider) &&
            descriptor.ImplementationType == typeof(RedisCacheServiceRider));
        using var provider = services.BuildServiceProvider();
        var cacheOptions = provider.GetRequiredService<IOptions<CacheServiceOptions>>().Value;
        Assert.Equal("app:{tenantId}:", cacheOptions.KeyPrefix);
        Assert.Equal(TimeSpan.FromMinutes(3), cacheOptions.MinimumLockRenewalDuration);
        Assert.Equal(0.75, cacheOptions.LockRenewalIntervalFactor);
    }

}
