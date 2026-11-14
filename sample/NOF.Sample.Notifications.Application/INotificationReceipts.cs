namespace NOF.Sample.Notifications.Application;

/// <summary>Application-owned port: writes are staged in the inbox's existing unit of work.</summary>
public interface INotificationReceipts
{
    void Add(MessageReceiptDto receipt);
    Task<IReadOnlyList<MessageReceiptDto>> ListAsync(Guid? orderId, CancellationToken cancellationToken);
}
