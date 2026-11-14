using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace NOF.Hosting;

/// <summary>Registers NOF services on native host builders.</summary>
public static partial class NOFHostingExtensions
{
    private static readonly object HostingConfiguredKey = new();

    /// <summary>Registers NOF hosting support and deployment options without replacing native host services.</summary>
    public static IHostApplicationBuilder AddNOFHosting(this IHostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        if (builder.Properties.ContainsKey(HostingConfiguredKey))
        {
            return builder;
        }
        builder.Properties[HostingConfiguredKey] = true;
        builder.Services.AddNOFHosting();
        builder.Services.Configure<DeploymentOptions>(options =>
        {
            options.ServiceName = builder.Configuration[NOFHostingConstants.Deployment.ConfigurationKeys.ServiceName]
                ?? builder.Environment.ApplicationName;
            if (uint.TryParse(builder.Configuration[NOFHostingConstants.Deployment.ConfigurationKeys.ServiceId], out var serviceId))
            {
                options.ServiceId = serviceId;
            }
            if (uint.TryParse(builder.Configuration[NOFHostingConstants.Deployment.ConfigurationKeys.InstanceId], out var instanceId))
            {
                options.InstanceId = instanceId;
            }
        });
        return builder;
    }
}
