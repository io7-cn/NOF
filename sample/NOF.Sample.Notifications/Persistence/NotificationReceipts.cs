using Microsoft.EntityFrameworkCore;
using NOF.Sample.Notifications.Application;

namespace NOF.Sample.Notifications.Persistence;

/// <summary>Stages projections in the very same EF scope that owns the durable inbox receipt.</summary>
public sealed class NotificationReceipts(NotificationsDbContext dbContext) : INotificationReceipts
{
    public void Add(MessageReceiptDto receipt) => dbContext.Receipts.Add(new MessageReceiptRow
    {
        Id = receipt.Id,
        OrderId = receipt.OrderId,
        Kind = receipt.Kind,
        Summary = receipt.Summary,
        ReceivedAtUtc = receipt.ReceivedAtUtc
    });

    public async Task<IReadOnlyList<MessageReceiptDto>> ListAsync(Guid? orderId, CancellationToken cancellationToken)
        => await dbContext.Receipts.AsNoTracking().Where(row => orderId == null || row.OrderId == orderId)
            .OrderByDescending(row => row.ReceivedAtUtc).ThenBy(row => row.Id).Take(100)
            .Select(row => new MessageReceiptDto(row.Id, row.OrderId, row.Kind, row.Summary, row.ReceivedAtUtc)).ToListAsync(cancellationToken);
}
