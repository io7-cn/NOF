using Microsoft.Extensions.Logging;
using NOF.Abstraction;
using NOF.Application;
using NOF.Contract;

namespace NOF.Sample.Notifications;

/// <summary>Application-private reaction to an event; no public service Contract method is needed.</summary>
public sealed class CountOrder(INotificationReceipts receipts) : OperationHandler<OrderCreated>
{
    public override Task HandleAsync(OrderCreated request, InvocationContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return receipts.RecordAsync($"order:{request.OrderId}", new(Guid.NewGuid(), request.OrderId,
            MessageReceiptKind.OrderMetrics, "Order counted by the metrics subscription", DateTime.UtcNow), cancellationToken);
    }
}
