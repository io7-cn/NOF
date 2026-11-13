using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using NOF.Abstraction;
using NOF.Application;
using System.Diagnostics.CodeAnalysis;

namespace NOF.Infrastructure;

/// <summary>Static metadata and a typed delivery delegate; persisted IDs never name CLR types.</summary>
public sealed record OutboxRegistration(Type ClrType, string MessageType, int SchemaVersion,
    Func<IServiceProvider, NOFOutboxMessage, OutboxDeliveryContext, CancellationToken, ValueTask> Deliver);

/// <summary>Host-local registrations frozen when the first message is staged or delivered.</summary>
public sealed class OutboxRegistry : Registry<OutboxRegistration>
{
    /// <inheritdoc />
    public override void Add(OutboxRegistration item)
    {
        if (Items.Any(existing => existing.ClrType == item.ClrType
            || existing.MessageType == item.MessageType && existing.SchemaVersion == item.SchemaVersion))
        {
            throw new InvalidOperationException("Each outbox message type and stable schema must have exactly one handler.");
        }
        base.Add(item);
    }
    /// <summary>Resolves the active enqueue schema for a CLR message type.</summary>
    public OutboxRegistration Get(Type messageType) => Freeze().SingleOrDefault(item => item.ClrType == messageType)
        ?? throw new InvalidOperationException($"No outbox handler is registered for '{messageType}'.");
    /// <summary>Resolves a persisted schema without reflection or dynamic type loading.</summary>
    public OutboxRegistration Get(string messageType, int schemaVersion)
        => Freeze().SingleOrDefault(item => item.MessageType == messageType && item.SchemaVersion == schemaVersion)
        ?? throw new OutboxPermanentFailureException($"No outbox handler for '{messageType}' schema {schemaVersion}. Drain or migrate pending records before upgrading.");
}

/// <summary>Binds a stable message schema to one scoped infrastructure handler.</summary>
public sealed class OutboxBuilder(IServiceCollection services, OutboxRegistry registry)
{
    /// <summary>Registers one message type and version; configure its JSON metadata through the normal serializer.</summary>
    public OutboxBuilder Add<TMessage, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] THandler>(
        string messageType, int schemaVersion = 1) where THandler : class, IOutboxHandler<TMessage>
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(messageType);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(schemaVersion);
        if (messageType.Length > 512)
        {
            throw new ArgumentOutOfRangeException(nameof(messageType), "Outbox message IDs cannot exceed 512 characters.");
        }
        registry.Add(new(typeof(TMessage), messageType, schemaVersion, static async (provider, record, context, ct) =>
        {
            object? value;
            try
            {
                value = provider.GetRequiredService<IObjectSerializer>().Deserialize(record.Payload, typeof(TMessage));
            }
            catch (System.Text.Json.JsonException error)
            {
                throw new OutboxPermanentFailureException("The outbox payload does not match its registered JSON schema.", error);
            }
            if (value is not TMessage message)
            {
                throw new OutboxPermanentFailureException("The outbox payload does not match its registered schema.");
            }
            await provider.GetRequiredService<THandler>().HandleAsync(message, context, ct).ConfigureAwait(false);
        }));
        services.TryAddScoped<THandler>();
        return this;
    }
}

/// <summary>Registers generic transactional intents independently of transport clients and operation bindings.</summary>
public static class OutboxServiceCollectionExtensions
{
    /// <summary>Registers host-owned schemas and scoped handlers.</summary>
    public static IServiceCollection AddOutbox(this IServiceCollection services, Action<OutboxBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        var registry = services.GetOrAddSingleton<OutboxRegistry>();
        services.TryAddScoped<IOutbox, TransactionalOutbox>();
        services.AddLogging();
        services.TryAddSingleton<ITenantProvider, HostTenantProvider>();
        services.TryAddSingleton<IObjectSerializer, JsonObjectSerializer>();
        services.AddOptions<TransactionalMessageOptions>()
            .Validate(options => !options.Outbox.RunWorkers ||
                options.Outbox.PollingInterval > TimeSpan.Zero && options.Outbox.BatchSize > 0
                && options.Outbox.MaxRetryCount > 0 && options.Outbox.RetryDelay >= TimeSpan.Zero
                && options.Outbox.ClaimTimeout > TimeSpan.Zero && options.Outbox.CleanupInterval > TimeSpan.Zero
                && options.Outbox.RetentionPeriod > TimeSpan.Zero, "Outbox transactional message options are invalid.")
            .ValidateOnStart();
        services.TryAddScoped<IOutboxStore, PersistenceOutboxStore>();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, OutboxMessageBackgroundService>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, OutboxCleanupBackgroundService>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IDbContextModelCreatingContributor, NOFOutboxMessageModelCreatingContributor>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IDbContextModelCreatingContributor, NOFOutboxOrderStateModelCreatingContributor>());
        services.TryAddScoped<OutboxOrderSequenceAllocator>(provider => new(provider.GetRequiredService<IPersistenceSession>(),
            provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<NOF.Hosting.DeploymentOptions>>()));
        configure(new(services, registry));
        return services;
    }
}
