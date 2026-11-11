using Microsoft.Extensions.DependencyInjection.Extensions;
using NOF.Abstraction;
using NOF.Application;
using NOF.Contract;
using System.Diagnostics.CodeAnalysis;
using System.Linq.Expressions;

namespace Microsoft.Extensions.DependencyInjection;

public static partial class NOFApplicationExtensions
{
    extension(IServiceCollection services)
    {
        /// <summary>
        /// Registers a scoped application service implementation and its service-to-implementation metadata.
        /// Hosts initialize the declared transport separately. Register servers before
        /// the <see cref="ApplicationServiceRegistry"/> is first read.
        /// </summary>
        /// <typeparam name="TApplicationService">The application service implementation implementation to register.</typeparam>
        /// <returns>The service collection for further configuration.</returns>
        public IServiceCollection AddApplicationService<
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TApplicationService>()
            where TApplicationService : ApplicationService, IApplicationServiceImplementation
        {
            ArgumentNullException.ThrowIfNull(services);

            services.TryAddScoped<TApplicationService>();
            var registry = services.GetOrAddSingleton<ApplicationServiceRegistry>();
            registry.RemoveWhere(existing => existing.ServiceType == TApplicationService.ServiceType);
            registry.Add(new ApplicationServiceRegistration(TApplicationService.ServiceType, typeof(TApplicationService))
            {
                HandlerMappings = TApplicationService.HandlerMappings
            });
            return services;
        }

        public IServiceCollection AddNOFApplication()
        {
            ArgumentNullException.ThrowIfNull(services);

            services.AddNOFAbstraction();
            services.AddNOFDomain();
            services.GetOrAddSingleton<MappingRegistry>();
            services.GetOrAddSingleton<CommandHandlerRegistry>();
            services.GetOrAddSingleton<NotificationHandlerRegistry>();
            services.GetOrAddSingleton<ApplicationServiceRegistry>();
            services.TryAddSingleton<IMapper, ExpressionMapper>();
            services.TryAddEnumerable(new ServiceDescriptor(
                typeof(IDaemonService),
                typeof(MapperAmbientDaemonService),
                ServiceLifetime.Scoped));
            return services;
        }

        /// <summary>
        /// Adds an explicit expression-based mapping registration.
        /// Register mappings before the first <see cref="IMapper"/> is resolved.
        /// </summary>
        public IServiceCollection AddMapping<TSource, TDestination>(
            Expression<Func<TSource, TDestination>> expression,
            string? name = null)
        {
            ArgumentNullException.ThrowIfNull(services);
            ArgumentNullException.ThrowIfNull(expression);

            services.AddNOFApplication();
            services.GetOrAddSingleton<MappingRegistry>()
                .Add(MappingRegistration.Of(expression, name));
            return services;
        }
    }
}
