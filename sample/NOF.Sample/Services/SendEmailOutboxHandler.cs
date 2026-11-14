using NOF.Infrastructure;
using NOF.Sample.Application;

namespace NOF.Sample.Services;

/// <summary>Delivers email intents through the infrastructure-owned client and queue.</summary>
public sealed class SendEmailOutboxHandler(IMessageClient client, MessageDelivery delivery) : OutboxHandler<SendEmail>
{
    /// <summary>The durable schema identity; independent of the CLR type and queue name.</summary>
    public const string MessageType = "sample.send-email";

    /// <inheritdoc />
    public override ValueTask HandleAsync(SendEmail message, OutboxDeliveryContext context, CancellationToken cancellationToken)
        => delivery.ExecuteAsync(message, context.Context,
            (headers, ct) => client.PublishAsync(string.Empty, RabbitMQDemoRoutes.MailQueue, message, headers, ct),
            cancellationToken, context);
}
