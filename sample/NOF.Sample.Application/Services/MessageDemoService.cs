using Microsoft.Extensions.Logging;
using NOF.Application;
using NOF.Contract;

namespace NOF.Sample.Application;

public partial class MessageDemoService : ApplicationService<IMessageDemoService>;

public sealed class SendMail(ILogger<SendMail> logger) : MessageDemoService.SendMail
{
    public override Task<Empty> HandleAsync(SendEmail request, InvocationContext context, CancellationToken cancellationToken)
    {
        logger.LogInformation("Demo mail accepted for {Recipient}: {Subject}", request.To, request.Subject);
        return Task.FromResult(Empty.Instance);
    }
}

public sealed class RecordOrder(ILogger<RecordOrder> logger) : MessageDemoService.RecordOrder
{
    public override Task<Empty> HandleAsync(OrderCreated notification, InvocationContext context, CancellationToken cancellationToken)
    {
        logger.LogInformation("Recorded order {OrderId}", notification.OrderId);
        return Task.FromResult(Empty.Instance);
    }
}
