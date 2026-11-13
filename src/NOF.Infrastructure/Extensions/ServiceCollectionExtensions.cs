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
        public IServiceCollection AddAuthenticationResourceServer(Action<AuthenticationResourceServerOptions> configureOptions)
        {
            ArgumentNullException.ThrowIfNull(configureOptions);

            services.Configure(configureOptions);
            services.AddHttpClient<HttpAuthorizationServerService>();
            services.TryAddScoped<IInboundAuthorizationHandler, DefaultInboundAuthorizationHandler>();
            services.TryAddScoped<IJwksService>(static serviceProvider =>
                serviceProvider.GetRequiredService<HttpAuthorizationServerService>());
            services.TryAddScoped<IAuthorizationServerMetadataService>(static serviceProvider =>
                serviceProvider.GetRequiredService<HttpAuthorizationServerService>());
            services.ReplaceOrAddSingleton<ResourceServerJwksCacheService, ResourceServerJwksCacheService>();
            services.AddInvocationFilter<AuthenticationInvocationFilter>(-200);
            return services;
        }

        [RequiresDynamicCode("The in-memory persistence provider exposes LINQ IQueryable over in-memory collections and is intended for tests/development, not Native AOT.")]
        [RequiresUnreferencedCode("The in-memory persistence provider snapshots arbitrary entity types via reflection and is intended for tests/development, not trimmed applications.")]
        public IServiceCollection AddInMemoryPersistence()
        {
            services.ReplaceOrAddSingleton<InMemoryPersistenceStore, InMemoryPersistenceStore>();
            services.ReplaceOrAddScoped<IPersistenceSession, InMemoryDbContext>();
            services.AddPersistenceServices();
            return services;
        }

        public IServiceCollection AddPersistenceServices()
        {
            services.AddOptions<TransactionalMessageOptions>();
            services.AddLogging();
            services.TryAddScoped<PersistenceUnitOfWork>();
            services.TryAddScoped<IInboxStore, PersistenceInboxStore>();
            services.TryAddScoped<IOutboxStore, PersistenceOutboxStore>();
            services.TryAddScoped<IUnitOfWork>(sp => sp.GetRequiredService<PersistenceUnitOfWork>());
            services.TryAddScoped<ITransactionManager>(sp => sp.GetRequiredService<PersistenceUnitOfWork>());
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
