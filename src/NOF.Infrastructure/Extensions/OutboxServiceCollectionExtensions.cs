using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NOF.Abstraction;
using NOF.Application;
using System.Diagnostics.CodeAnalysis;

namespace NOF.Infrastructure;

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
