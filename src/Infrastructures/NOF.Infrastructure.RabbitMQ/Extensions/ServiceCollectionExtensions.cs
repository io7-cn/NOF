using Microsoft.Extensions.DependencyInjection;
using NOF.Application;
using NOF.Infrastructure;
using NOF.Infrastructure.RabbitMQ;

namespace NOF.Hosting;

public static partial class NOFInfrastructureRabbitMQExtensions
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddRabbitMQBackplane()
        {
            services.ReplaceOrAddSingleton<RabbitMQConnectionManager, RabbitMQConnectionManager>();
            services.ReplaceOrAddSingleton<IBackplane, RabbitMQBackplane>();
            return services;
        }

        public IServiceCollection AddRabbitMQBackplane(Action<RabbitMQOptions> configureOptions)
        {
            ArgumentNullException.ThrowIfNull(configureOptions);

            services.Configure(configureOptions);
            return services.AddRabbitMQBackplane();
        }

        /// <summary>Registers native connection access and optional queue consumers with durable Inbox processing.</summary>
        public IServiceCollection AddRabbitMQ(Action<RabbitMQOptions> configureOptions, Action<RabbitMQEndpointsBuilder>? configureEndpoints = null)
        {
            ArgumentNullException.ThrowIfNull(configureOptions);

            services.Configure(configureOptions);
            services.AddOptions<RabbitMQOptions>()
                .Validate(
                    static options => !string.IsNullOrWhiteSpace(options.HostName),
                    "RabbitMQ HostName must be configured.")
                .Validate(
                    static options => options.Port >= 0,
                    "RabbitMQ Port must be greater than or equal to zero.")
                .Validate(
                    static options => options.PrefetchCount > 0,
                    "RabbitMQ PrefetchCount must be greater than zero.")
                .ValidateOnStart();
            services.ReplaceOrAddSingleton<RabbitMQConnectionManager, RabbitMQConnectionManager>();
            if (configureEndpoints is not null)
            {
                services.AddInbox();
                configureEndpoints(new(services, services.GetOrAddSingleton<RabbitMQEndpointRegistry>()));
                services.AddHostedService<RabbitMQConsumerHostedService>();
            }
            return services;
        }
    }
}
