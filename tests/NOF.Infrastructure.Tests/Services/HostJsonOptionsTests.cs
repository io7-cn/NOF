using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NOF.Hosting;
using System.Text.Json;
using System.Text.Json.Serialization;
using Xunit;

namespace NOF.Infrastructure.Tests.Services;

public sealed class HostJsonOptionsTests
{
    [Fact]
    public void FrozenSerializer_DoesNotFreezeOrModifyAnotherHostsOptions()
    {
        using var first = CreateHost(JsonNamingPolicy.SnakeCaseLower, beforeRegistration: true);
        var a = first.GetRequiredService<IObjectSerializer>();
        Assert.Equal("{\"display_name\":\"first\"}", System.Text.Encoding.UTF8.GetString(a.Serialize(new HostJsonPayload("first")).Span));
        using var second = CreateHost(null, beforeRegistration: false);
        var b = second.GetRequiredService<IObjectSerializer>();
        Assert.Equal("{\"DisplayName\":\"second\"}", System.Text.Encoding.UTF8.GetString(b.Serialize(new HostJsonPayload("second")).Span));
        Assert.Equal("{\"display_name\":\"again\"}", System.Text.Encoding.UTF8.GetString(a.Serialize(new HostJsonPayload("again")).Span));
        Assert.NotSame(first.GetRequiredService<IOptions<JsonSerializerOptions>>().Value,
            second.GetRequiredService<IOptions<JsonSerializerOptions>>().Value);
    }

    [Fact]
    public void Selectors_AssembleOnlySelectedCapabilities_AndPreserveHostIsolation()
    {
        var services = new ServiceCollection();
        services.AddCacheService().AddMemoryCacheService();
        services.AddCacheService(options => options.KeyPrefix = "host-a:").AddMemoryCacheService();
        services.AddObjectStorage().AddMemoryObjectStorage();
        services.AddBackplane().AddMemoryBackplane();
        Assert.Single(services, item => item.ServiceType == typeof(NOF.Application.ICacheService));
        using var first = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        using var other = new ServiceCollection().AddCacheService().AddMemoryCacheService().Services.BuildServiceProvider();
        Assert.NotSame(first.GetRequiredService<MemoryCacheServiceRiderState>(), other.GetRequiredService<MemoryCacheServiceRiderState>());
        Assert.Equal("host-a:", first.GetRequiredService<IOptions<CacheServiceOptions>>().Value.KeyPrefix);
        Assert.DoesNotContain(services, item => item.ServiceType == typeof(IInboxStore) || item.ServiceType == typeof(IOutboxStore));
    }

    private static ServiceProvider CreateHost(JsonNamingPolicy? naming, bool beforeRegistration)
    {
        var services = new ServiceCollection();
        void Configure(JsonSerializerOptions options)
        {
            options.PropertyNamingPolicy = naming;
            options.TypeInfoResolverChain.Clear();
            options.TypeInfoResolverChain.Add(HostJsonContext.Default);
        }
        if (beforeRegistration)
        {
            services.Configure<JsonSerializerOptions>(Configure);
            services.AddNOFJson();
        }
        else
        {
            services.AddNOFJson(Configure);
        }
        services.AddNOFJson();
        return services.BuildServiceProvider();
    }
}

public sealed record HostJsonPayload(string DisplayName);
[JsonSerializable(typeof(HostJsonPayload))]
public partial class HostJsonContext : JsonSerializerContext;
