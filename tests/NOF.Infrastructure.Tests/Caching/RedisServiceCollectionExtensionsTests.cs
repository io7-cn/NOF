using Microsoft.Extensions.DependencyInjection;
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

        services.AddCacheService().AddRedisCache(
            new ConfigurationOptions
            {
                EndPoints = { "localhost:6379" },
                AbortOnConnectFail = false
            },
            options => options.MinimumLockRenewalDuration = TimeSpan.FromMinutes(3));

        Assert.Contains(services, descriptor => descriptor.ServiceType == typeof(IConnectionMultiplexer));
        Assert.Contains(services, descriptor =>
            descriptor.ServiceType == typeof(ICacheServiceRider) &&
            descriptor.ImplementationType == typeof(RedisCacheServiceRider));
    }

}
