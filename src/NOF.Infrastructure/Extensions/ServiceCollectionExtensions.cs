using Microsoft.Extensions.DependencyInjection.Extensions;
using NOF.Application;
using NOF.Domain;
using NOF.Infrastructure;
using System.Diagnostics.CodeAnalysis;

namespace Microsoft.Extensions.DependencyInjection;

// ReSharper disable once InconsistentNaming
public static partial class NOFInfrastructureExtensions
{
    /// <param name="services">The <see cref="IServiceCollection"/> to add the service to.</param>
    extension(IServiceCollection services)
    {
        /// <summary>Registers the default message-only memory unit of work; business ports select their own persistence.</summary>
        public IServiceCollection AddInMemoryMessagingPersistence()
        {
            services.AddOptions<TransactionalMessageOptions>();
            services.TryAddSingleton<InMemoryMessageDatabase>();
            services.TryAddScoped<InMemoryMessagePersistence>();
            services.TryAddScoped<IUnitOfWork>(sp => sp.GetRequiredService<InMemoryMessagePersistence>());
            services.TryAddScoped<ITransactionManager>(sp => sp.GetRequiredService<InMemoryMessagePersistence>());
            services.TryAddKeyedScoped<IInboxStore>(typeof(IInboxStore), (sp, _) => sp.GetRequiredService<InMemoryMessagePersistence>());
            services.TryAddKeyedScoped<IOutboxStore>(typeof(IOutboxStore), (sp, _) => sp.GetRequiredService<InMemoryMessagePersistence>());
            return services;
        }

        /// <summary>
        /// Registers a hosted service backed by an asynchronous delegate.
        /// </summary>
        public IServiceCollection AddHostedService(Func<IServiceProvider, CancellationToken, Task> startAction)
        {
            return services.AddHostedService(sp => new DelegateBackgroundService(sp, startAction));
        }

        /// <summary>
        /// Registers a hosted service backed by a synchronous delegate.
        /// </summary>
        public IServiceCollection AddHostedService(Action<IServiceProvider, CancellationToken> startAction)
            => services.AddHostedService((sp, ct) => { startAction(sp, ct); return Task.CompletedTask; });
    }
}
