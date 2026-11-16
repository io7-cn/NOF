namespace NOF.Sample.Notifications;

/// <summary>Application-owned projection persistence and atomic business-key deduplication.</summary>
public interface INotificationReceipts
{
    Task RecordAsync(string idempotencyKey, MessageReceiptDto receipt, CancellationToken cancellationToken);
    Task<IReadOnlyList<MessageReceiptDto>> ListAsync(Guid? orderId, CancellationToken cancellationToken);
}
