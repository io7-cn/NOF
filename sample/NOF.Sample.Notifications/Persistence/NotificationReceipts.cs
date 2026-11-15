using Microsoft.EntityFrameworkCore;

namespace NOF.Sample.Notifications.Persistence;

/// <summary>Commits one projection atomically; its unique business key makes repeated deliveries harmless.</summary>
public sealed class NotificationReceipts(NotificationsDbContext dbContext) : INotificationReceipts
{
    public async Task RecordAsync(string idempotencyKey, MessageReceiptDto receipt, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);
        await dbContext.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO "Receipts" ("Id", "IdempotencyKey", "OrderId", "Kind", "Summary", "ReceivedAtUtc", "TenantId")
            VALUES ({receipt.Id}, {idempotencyKey}, {receipt.OrderId}, {(int)receipt.Kind}, {receipt.Summary}, {receipt.ReceivedAtUtc}, {dbContext.CurrentTenantId})
            ON CONFLICT ("IdempotencyKey", "Kind", "TenantId") DO NOTHING
            """, cancellationToken);
    }

    public async Task<IReadOnlyList<MessageReceiptDto>> ListAsync(Guid? orderId, CancellationToken cancellationToken)
        => await dbContext.Receipts.AsNoTracking().Where(row => orderId == null || row.OrderId == orderId)
            .OrderByDescending(row => row.ReceivedAtUtc).ThenBy(row => row.Id).Take(100)
            .Select(row => new MessageReceiptDto(row.Id, row.OrderId, row.Kind, row.Summary, row.ReceivedAtUtc)).ToListAsync(cancellationToken);
}
