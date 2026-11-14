using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace NOF.Infrastructure;

/// <summary>Configures the framework payload serializer independently for each host.</summary>
public static class JsonServiceCollectionExtensions
{
    /// <summary>Registers JSON payload serialization; repeated configuration uses the native options pipeline.</summary>
    public static IServiceCollection AddNOFJson(this IServiceCollection services, Action<JsonSerializerOptions>? configure = null)
    {
        services.AddOptions<JsonSerializerOptions>();
        if (!services.Any(descriptor => descriptor.ImplementationType == typeof(ConfigureNOFJsonOptions)))
        {
            services.Insert(0, ServiceDescriptor.Singleton<IConfigureOptions<JsonSerializerOptions>, ConfigureNOFJsonOptions>());
        }
        services.TryAddSingleton<IObjectSerializer, JsonObjectSerializer>();
        if (configure is not null)
        {
            services.Configure(configure);
        }
        return services;
    }
}

/// <summary>Supplies default metadata before host-specific JSON configuration.</summary>
public sealed class ConfigureNOFJsonOptions : IConfigureOptions<JsonSerializerOptions>
{
    /// <inheritdoc />
    public void Configure(JsonSerializerOptions options)
    {
        var defaults = JsonSerializerOptions.CreateNOF();
        options.PropertyNamingPolicy = defaults.PropertyNamingPolicy;
        options.PropertyNameCaseInsensitive = defaults.PropertyNameCaseInsensitive;
        options.ReferenceHandler = defaults.ReferenceHandler;
        options.DefaultIgnoreCondition = defaults.DefaultIgnoreCondition;
        options.TypeInfoResolver = defaults.TypeInfoResolver;
    }
}
