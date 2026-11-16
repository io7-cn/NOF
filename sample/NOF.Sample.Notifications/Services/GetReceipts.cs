using Microsoft.Extensions.Logging;
using NOF.Abstraction;
using NOF.Application;
using NOF.Contract;

namespace NOF.Sample.Notifications;

public sealed class GetReceipts(INotificationReceipts receipts) : MessageDemoService.GetReceipts
{
    public override async Task<MessageReceiptsResponse> HandleAsync(GetMessageReceiptsRequest request, InvocationContext context, CancellationToken cancellationToken)
        => new(await receipts.ListAsync(request.OrderId, cancellationToken));
}
