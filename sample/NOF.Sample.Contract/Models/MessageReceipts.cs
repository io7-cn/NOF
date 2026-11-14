namespace NOF.Sample;

public enum MessageReceiptKind
{
    Mail,
    OrderAudit,
    OrderMetrics
}
public sealed record GetMessageReceiptsRequest(Guid? OrderId = null);
public sealed record MessageReceiptDto(Guid Id, Guid? OrderId, MessageReceiptKind Kind, string Summary, DateTime ReceivedAtUtc);
public sealed record MessageReceiptsResponse(IReadOnlyList<MessageReceiptDto> Items);
