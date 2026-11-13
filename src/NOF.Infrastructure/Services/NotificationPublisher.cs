using NOF.Application;
using NOF.Contract;
using NOF.Hosting;
using System.Diagnostics;

namespace NOF.Infrastructure;

public sealed class NotificationPublisher : INotificationPublisher
{
    private readonly INotificationRider _rider;
    private readonly MessageDelivery _delivery;
    private readonly IDbContext _dbContext;
    private readonly IObjectSerializer _objectSerializer;
    private readonly OutboxOrderSequenceAllocator _orderSequenceAllocator;

    public NotificationPublisher(
        INotificationRider rider,
        MessageDelivery delivery,
        IDbContext dbContext,
        IObjectSerializer objectSerializer,
        OutboxOrderSequenceAllocator orderSequenceAllocator)
    {
        _rider = rider;
        _delivery = delivery;
        _dbContext = dbContext;
        _objectSerializer = objectSerializer;
        _orderSequenceAllocator = orderSequenceAllocator;
    }

    public async Task DeferPublishAsync(object notification, Type notificationType, Context context, CancellationToken cancellationToken = default)
        => await DeferPublishCoreAsync(notification, notificationType, null, context, false, cancellationToken);

    public async Task DeferPublishOrderedAsync(
        object notification,
        Type notificationType,
        string orderKey,
        Context context,
        bool completesOrderKey = false,
        CancellationToken cancellationToken = default)
        => await DeferPublishCoreAsync(notification, notificationType, orderKey, context, completesOrderKey, cancellationToken);

    private async Task DeferPublishCoreAsync(
        object notification,
        Type notificationType,
        string? orderKey,
        Context context,
        bool completesOrderKey,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(notification);
        ArgumentNullException.ThrowIfNull(notificationType);
        ArgumentNullException.ThrowIfNull(context);
        await _delivery.ExecuteAsync(notification, context, async (headers, token) =>
        {
            var dispatchRoutes = _objectSerializer.SerializeToText(
                new[] { notificationType.DisplayName },
                typeof(string[]));

            var order = orderKey is null
                ? (OutboxOrder?)null
                : await _orderSequenceAllocator.AllocateAsync(orderKey, completesOrderKey, token);

            _dbContext.Set<NOFOutboxMessage>().Add(NOFOutboxMessage.Create(
                OutboxMessageType.Notification,
                dispatchRoutes,
                _objectSerializer.Serialize(notification).ToArray(),
                _objectSerializer.SerializeToText(headers, typeof(Dictionary<string, string?>)),
                Activity.Current?.ToTraceParent(),
                order));
        }, cancellationToken).ConfigureAwait(false);
    }

    public async Task PublishAsync(object notification, Type notificationType, Context context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(notification);
        ArgumentNullException.ThrowIfNull(notificationType);
        ArgumentNullException.ThrowIfNull(context);
        await _delivery.ExecuteAsync(notification, context, async (headers, token) =>
        {
            var payload = _objectSerializer.Serialize(notification, notification.GetType());
            await _rider.PublishAsync(payload, notificationType.DisplayName, headers, token).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);
    }

}
