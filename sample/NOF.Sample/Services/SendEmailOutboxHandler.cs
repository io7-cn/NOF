using NOF.Infrastructure;
using NOF.Sample.Application;

namespace NOF.Sample.Services;

/// <summary>Delivers email intents through the infrastructure-owned client and queue.</summary>
public sealed class SendEmailOutboxHandler(IMessageClient client) : OutboxHandler<SendEmail>
{

    /// <inheritdoc />
    public override ValueTask HandleAsync(SendEmail message, OutboxDeliveryContext context, CancellationToken cancellationToken)
        => client.PublishAsync(string.Empty, RabbitMQDemoRoutes.MailQueue, message, context.Headers, cancellationToken);
}
