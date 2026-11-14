using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using NOF.Application;
using NOF.Infrastructure.RabbitMQ;
using RabbitMQ.Client;

namespace NOF.Hosting;

public static partial class NOFInfrastructureRabbitMQExtensions
{
    extension(IServiceCollection services)
    {
        /// <summary>Registers lazy native connection access; no topology, Inbox or consumers are enabled.</summary>
        public IServiceCollection AddRabbitMQ(Action<ConnectionFactory>? configureConnection = null)
        {
            var options = services.AddOptions<ConnectionFactory>();
            if (configureConnection is not null)
            {
                options.Configure(configureConnection);
            }
            services.TryAddSingleton<IConnectionFactory>(provider => provider.GetRequiredService<IOptions<ConnectionFactory>>().Value);
            services.TryAddSingleton<RabbitMQConnection>();
            return services;
        }

        /// <summary>Appends fluent native topology and queue declarations; repeated calls compose before startup.</summary>
        public IServiceCollection ConfigureRabbitMQ(Action<RabbitMQBuilder> configure)
        {
            ArgumentNullException.ThrowIfNull(configure);
            if (services.IsReadOnly)
            {
                throw new InvalidOperationException("Configure RabbitMQ before building the host.");
            }
            services.AddLogging();
            configure(services.GetOrAddSingleton(() => new RabbitMQBuilder(services)));
            services.AddHostedService<RabbitMQConsumerHostedService>();
            return services;
        }

        /// <summary>Registers the independent transient backplane using the same native connection registration.</summary>
        public IServiceCollection AddRabbitMQBackplane()
        {
            services.AddRabbitMQ();
            services.ReplaceOrAddSingleton<IBackplane, RabbitMQBackplane>();
            return services;
        }
    }
}
