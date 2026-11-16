namespace NOF.Sample;

public sealed record GetMessageReceiptsRequest(Guid? OrderId = null);
public sealed record MessageReceiptsResponse(IReadOnlyList<MessageReceiptDto> Items);
