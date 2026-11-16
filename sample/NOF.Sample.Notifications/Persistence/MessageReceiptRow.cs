using Microsoft.EntityFrameworkCore;
using NOF.Infrastructure.EntityFrameworkCore;

namespace NOF.Sample.Notifications.Persistence;

public sealed class MessageReceiptRow
{
    public Guid Id { get; set; }
    public string IdempotencyKey { get; set; } = "";
    public Guid? OrderId { get; set; }
    public MessageReceiptKind Kind { get; set; }
    public string Summary { get; set; } = "";
    public DateTime ReceivedAtUtc { get; set; }
}
