using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using NOF.Application;

namespace NOF.Infrastructure;

/// <summary>Assembles transactional message storage and processing independently of transport topology.</summary>
public static class TransactionalMessageServiceCollectionExtensions
{
    /// <summary>
    /// Enables Inbox receipt processing and Outbox staging with shared persistence, transactions,
    /// retries and cleanup. Repeated calls preserve existing stores and register each worker once.
    /// Configure <see cref="TransactionalMessageOptions"/> to stop either direction's workers;
    /// register handler application parts and transport endpoints separately.
    /// </summary>
    public static IServiceCollection AddTransactionalMessages(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.GetOrAddSingleton<InboxEndpointRegistry>();
        services.GetOrAddSingleton<OutboxRegistry>();
        services.AddLogging();
        services.TryAddSingleton<ITenantProvider, HostTenantProvider>();
        services.AddNOFJson();
        services.AddOptions<TransactionalMessageOptions>()
            .Validate(options => IsValid(options.Inbox), "Inbox transactional message options are invalid.")
            .Validate(options => IsValid(options.Outbox), "Outbox transactional message options are invalid.")
            .ValidateOnStart();
        services.AddInMemoryMessagingPersistence();
        services.TryAddScoped<IInboxStore>(sp => sp.GetRequiredKeyedService<IInboxStore>(typeof(IInboxStore)));
        services.TryAddScoped<IOutboxStore>(sp => sp.GetRequiredKeyedService<IOutboxStore>(typeof(IOutboxStore)));
        services.TryAddScoped<IOutbox, TransactionalOutbox>();
        services.TryAddSingleton<MessageInboxReceiver>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, InboxMessageBackgroundService>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, InboxCleanupBackgroundService>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, OutboxMessageBackgroundService>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, OutboxCleanupBackgroundService>());
        return services;

        static bool IsValid(TransactionalMessageProcessorOptions options) => !options.RunWorkers ||
            options.PollingInterval > TimeSpan.Zero && options.BatchSize > 0
            && options.MaxRetryCount > 0 && options.RetryDelay >= TimeSpan.Zero
            && options.ClaimTimeout > TimeSpan.Zero && options.CleanupInterval > TimeSpan.Zero
            && options.RetentionPeriod > TimeSpan.Zero;
    }
}
