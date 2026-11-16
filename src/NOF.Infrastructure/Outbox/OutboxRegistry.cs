using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NOF.Abstraction;
using NOF.Application;
using System.Diagnostics.CodeAnalysis;

namespace NOF.Infrastructure;

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
