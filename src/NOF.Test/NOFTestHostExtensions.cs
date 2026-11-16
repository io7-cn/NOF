using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NOF.Hosting;
using NOF.Infrastructure;

namespace NOF.Test;

/// <summary>Configures native hosts for isolated application tests.</summary>
public static class NOFTestHostExtensions
{
    /// <summary>Selects in-memory defaults and ephemeral object storage for a native test host.</summary>
    public static HostApplicationBuilder AddNOFTest(this HostApplicationBuilder builder)
    {
        builder.AddNOF();
        builder.Services.AddObjectStorage().AddMemoryObjectStorage();
        builder.Services.AddCacheService().AddMemoryCacheService();
        builder.Services.AddIdGenerator().AddSnowflakeIdGenerator();
        return builder;
    }

    /// <summary>Builds the native host without starting background services.</summary>
    public static NOFTestHost BuildTestHost(this HostApplicationBuilder builder) => new(builder.Build());

    /// <summary>Adds a marker's generated registrations to the native builder.</summary>
    public static HostApplicationBuilder AddApplicationPartOf<TMarker>(this HostApplicationBuilder builder)
    {
        builder.AddApplicationPart(typeof(TMarker).Assembly);
        return builder;
    }
}
