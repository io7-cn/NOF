using Microsoft.Extensions.Logging;
using NOF.Abstraction;
using NOF.Application;
using NOF.Contract;

namespace NOF.Sample.Notifications;

public sealed class RecordOrder(INotificationReceipts receipts) : MessageDemoService.RecordOrder
{
    public override Task HandleAsync(OrderCreated request, InvocationContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return receipts.RecordAsync($"order:{request.OrderId}", new(Guid.NewGuid(), request.OrderId,
            MessageReceiptKind.OrderAudit, "Order audit recorded", DateTime.UtcNow), cancellationToken);
    }
}
