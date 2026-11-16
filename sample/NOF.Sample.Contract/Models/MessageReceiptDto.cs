namespace NOF.Sample;

public sealed record MessageReceiptDto(Guid Id, Guid? OrderId, MessageReceiptKind Kind, string Summary, DateTime ReceivedAtUtc);
