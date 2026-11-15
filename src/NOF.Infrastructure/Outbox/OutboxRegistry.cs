using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NOF.Abstraction;
using NOF.Application;
using System.Diagnostics.CodeAnalysis;

namespace NOF.Infrastructure;

/// <summary>Static metadata and a typed delivery delegate; inferred type identities resolve only through registered delegates, never dynamic type loading.</summary>
public sealed record OutboxRegistration(Type ClrType, string MessageType,
    Func<IServiceProvider, NOFOutboxMessage, OutboxDeliveryContext, CancellationToken, ValueTask> Deliver);

/// <summary>Host-local registrations frozen when the first message is staged or delivered.</summary>
public sealed class OutboxRegistry : Registry<OutboxRegistration>
{
    /// <inheritdoc />
    public override void Add(OutboxRegistration item)
    {
        if (Items.Any(existing => existing.ClrType == item.ClrType
            || existing.MessageType == item.MessageType))
        {
            throw new InvalidOperationException("Each outbox message type and stable schema must have exactly one handler.");
        }
        base.Add(item);
    }
    /// <summary>Resolves the active enqueue schema for a CLR message type.</summary>
    public OutboxRegistration Get(Type messageType) => Freeze().SingleOrDefault(item => item.ClrType == messageType)
        ?? throw new InvalidOperationException($"No outbox handler is registered for '{messageType}'.");
    /// <summary>Resolves a persisted schema without reflection or dynamic type loading.</summary>
    public OutboxRegistration Get(string messageType)
        => Freeze().SingleOrDefault(item => item.MessageType == messageType)
        ?? throw new OutboxPermanentFailureException($"No outbox handler for '{messageType}'. Drain or migrate pending records before upgrading.");
}

/// <summary>Registers generic transactional intents independently of transport clients and operation bindings.</summary>
public static class OutboxServiceCollectionExtensions
{
    /// <summary>Registers one schema and scoped handler without enabling Outbox. Generated application parts call this method.</summary>
    public static IServiceCollection AddOutboxHandler<TMessage, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] THandler>(
        this IServiceCollection services) where THandler : OutboxHandler<TMessage>
    {
        ArgumentNullException.ThrowIfNull(services);
        var messageType = typeof(TMessage).ToString();
        if (messageType.Length > 512)
        {
            throw new InvalidOperationException("Inferred Outbox message IDs cannot exceed 512 characters.");
        }
        services.GetOrAddSingleton<OutboxRegistry>().Add(new(typeof(TMessage), messageType, static async (provider, record, context, ct) =>
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
        return services;
    }

}
