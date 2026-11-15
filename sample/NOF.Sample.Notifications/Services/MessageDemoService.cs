using Microsoft.Extensions.Logging;
using NOF.Application;
using NOF.Contract;

namespace NOF.Sample.Notifications;

public partial class MessageDemoService : ApplicationService<IMessageDemoService>;

/// <summary>Simulates mail processing; this demo records acceptance rather than calling an SMTP server.</summary>
public sealed class SendMail(INotificationReceipts receipts, ILogger<SendMail> logger) : MessageDemoService.SendMail
{
    public override Task HandleAsync(SendEmail request, InvocationContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        receipts.Add(new(Guid.NewGuid(), request.OrderId, MessageReceiptKind.Mail, $"Demo mail for {request.To}: {request.Subject}", DateTime.UtcNow));
        logger.LogInformation("Demo mail accepted for order {OrderId}, recipient {Recipient}", request.OrderId, request.To);
        return Task.CompletedTask;
    }
}

public sealed class RecordOrder(INotificationReceipts receipts) : MessageDemoService.RecordOrder
{
    public override Task HandleAsync(OrderCreated request, InvocationContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        receipts.Add(new(Guid.NewGuid(), request.OrderId, MessageReceiptKind.OrderAudit, "Order audit recorded", DateTime.UtcNow));
        return Task.CompletedTask;
    }
}

/// <summary>Application-private reaction to an event; no public service Contract method is needed.</summary>
public sealed class CountOrder(INotificationReceipts receipts) : OperationHandler<OrderCreated>
{
    public override Task HandleAsync(OrderCreated request, InvocationContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        receipts.Add(new(Guid.NewGuid(), request.OrderId, MessageReceiptKind.OrderMetrics, "Order counted by the metrics subscription", DateTime.UtcNow));
        return Task.CompletedTask;
    }
}

public sealed class GetReceipts(INotificationReceipts receipts) : MessageDemoService.GetReceipts
{
    public override async Task<MessageReceiptsResponse> HandleAsync(GetMessageReceiptsRequest request, InvocationContext context, CancellationToken cancellationToken)
        => new(await receipts.ListAsync(request.OrderId, cancellationToken));
}
