using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using System.Text.Json;

namespace NOF.Infrastructure;

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
