using NOF.Application;
using System.Diagnostics.CodeAnalysis;

namespace NOF.Infrastructure;

/// <summary>Metadata for an incoming notification delivery.</summary>
public sealed class NotificationInboundContext : InvocationContext
{
    /// <inheritdoc />
    public override InvocationKind Kind => InvocationKind.Notification;

    /// <summary>Creates a delivery context.</summary>
    public NotificationInboundContext() { }

    [SetsRequiredMembers]
    private NotificationInboundContext(IReadOnlyDictionary<object, object?> items, NotificationInboundContext source) : base(items, source) { }

    /// <inheritdoc />
    protected override NOF.Contract.Context Clone(IReadOnlyDictionary<object, object?> items) => new NotificationInboundContext(items, this);
}
