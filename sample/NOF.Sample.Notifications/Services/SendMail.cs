using Microsoft.Extensions.Logging;
using NOF.Abstraction;
using NOF.Application;
using NOF.Contract;

namespace NOF.Sample.Notifications;

/// <summary>Simulates mail processing; this demo records acceptance rather than calling an SMTP server.</summary>
public sealed class SendMail(INotificationReceipts receipts, ILogger<SendMail> logger) : MessageDemoService.SendMail
{
    public override async Task HandleAsync(SendEmail request, InvocationContext context, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var key = request.OrderId is { } orderId ? $"order:{orderId}"
            : context.Context[NOFAbstractionConstants.Transport.Headers.MessageId]?.GetValue<string>()
                ?? throw new InvalidOperationException("A stable message ID is required for a mail without an order ID.");
        await receipts.RecordAsync(key, new(Guid.NewGuid(), request.OrderId, MessageReceiptKind.Mail,
            $"Demo mail for {request.To}: {request.Subject}", DateTime.UtcNow), cancellationToken);
        logger.LogInformation("Demo mail accepted for order {OrderId}, recipient {Recipient}", request.OrderId, request.To);
    }
}
