using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using NOF.Abstraction;
using NOF.Application;

namespace NOF.Infrastructure;

/// <summary>Assembles durable receipt storage and processing independently of any transport topology.</summary>
public static class InboxServiceCollectionExtensions
{
    /// <summary>Enables Inbox storage, transactions, retry and cleanup; adapters supply keyed IInboxHandler entries.</summary>
    public static IServiceCollection AddInbox(this IServiceCollection services)
    {
        services.AddLogging();
        services.TryAddSingleton<ITenantProvider, HostTenantProvider>();
        services.AddNOFJson();
        services.AddOptions<TransactionalMessageOptions>()
            .Validate(options => !options.Inbox.RunWorkers ||
                options.Inbox.PollingInterval > TimeSpan.Zero && options.Inbox.BatchSize > 0
                && options.Inbox.MaxRetryCount > 0 && options.Inbox.RetryDelay >= TimeSpan.Zero
                && options.Inbox.ClaimTimeout > TimeSpan.Zero && options.Inbox.CleanupInterval > TimeSpan.Zero
                && options.Inbox.RetentionPeriod > TimeSpan.Zero, "Inbox transactional message options are invalid.")
            .ValidateOnStart();
        services.AddInMemoryMessagingPersistence();
        services.TryAddScoped(sp => sp.GetRequiredKeyedService<IInboxStore>(typeof(IInboxStore)));
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, InboxMessageBackgroundService>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, InboxCleanupBackgroundService>());
        services.TryAddSingleton<MessageInboxReceiver>();
        return services;
    }
}
