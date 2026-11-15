using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using NOF.Application;

namespace NOF.Infrastructure;

/// <summary>Assembles transactional message storage and processing independently of transport topology.</summary>
public static class OutboxRegistrationExtensions
{
    /// <summary>
    /// Enables Outbox staging with persistence, transactions,
    /// retries and cleanup. Repeated calls preserve existing stores and register each worker once.
    /// Configure <see cref="OutboxOptions"/> to stop processing and cleanup workers;
    /// register handler application parts and transport endpoints separately.
    /// </summary>
    public static IServiceCollection AddOutbox(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.GetOrAddSingleton<OutboxRegistry>();
        services.AddLogging();
        services.TryAddSingleton<ITenantProvider, HostTenantProvider>();
        services.AddNOFJson();
        services.AddOptions<OutboxOptions>()
            .Validate(options => IsValid(options), "Outbox transactional message options are invalid.")
            .ValidateOnStart();
        services.AddInMemoryMessagingPersistence();
        services.TryAddScoped<IOutboxStore>(sp => sp.GetRequiredKeyedService<IOutboxStore>(typeof(IOutboxStore)));
        services.TryAddScoped<IOutbox, TransactionalOutbox>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, OutboxMessageBackgroundService>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, OutboxCleanupBackgroundService>());
        return services;

        static bool IsValid(OutboxOptions options) => !options.RunWorkers ||
            options.PollingInterval > TimeSpan.Zero && options.BatchSize > 0
            && options.MaxRetryCount > 0 && options.RetryDelay >= TimeSpan.Zero
            && options.ClaimTimeout > TimeSpan.Zero && options.CleanupInterval > TimeSpan.Zero
            && options.RetentionPeriod > TimeSpan.Zero;
    }
}
