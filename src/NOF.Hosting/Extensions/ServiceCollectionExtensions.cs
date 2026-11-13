using Microsoft.Extensions.DependencyInjection.Extensions;
using NOF.Hosting;

namespace Microsoft.Extensions.DependencyInjection;

public static partial class NOFHostingExtensions
{
    /// <param name="services">The <see cref="IServiceCollection"/> to add the service to.</param>
    extension(IServiceCollection services)
    {
        public IServiceCollection AddNOFHosting()
        {
            services.AddNOFAbstraction();
            services.AddOptions<DeploymentOptions>().Validate(static options => !string.IsNullOrWhiteSpace(options.ServiceName), "ServiceName must not be blank.");
            services.TryAddTransient(typeof(Lazy<>), typeof(NOFLazy<>));
            return services;
        }

    }
}
