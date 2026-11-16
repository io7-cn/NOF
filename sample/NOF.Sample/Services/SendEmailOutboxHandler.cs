using NOF.Infrastructure;
using NOF.Infrastructure.RabbitMQ;

namespace NOF.Sample.Services;

/// <summary>Delivers email intents through the infrastructure-owned client and queue.</summary>
public sealed class SendEmailOutboxHandler(RabbitMQMessageClient client, DemoDeliveryControl control) : OutboxHandler<SendEmail>
{

    /// <inheritdoc />
    public override async ValueTask HandleAsync(SendEmail message, OutboxDeliveryContext context, CancellationToken cancellationToken)
    {
        await control.BeforeDeliveryAsync(cancellationToken);
        await client.PublishAsync(string.Empty, RabbitMQDemoRoutes.MailQueue, message, context.Headers, cancellationToken);
    }
}
