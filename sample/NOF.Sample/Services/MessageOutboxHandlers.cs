using NOF.Infrastructure;
using NOF.Sample.Application;

namespace NOF.Sample.Services;

/// <summary>The host chooses an explicit client and physical route for each durable intent.</summary>
public sealed class SendEmailOutboxHandler(MemoryMessageClient client) : IOutboxHandler<SendEmail>
{
    public async ValueTask HandleAsync(SendEmail message, OutboxDeliveryContext context, CancellationToken cancellationToken)
        => await client.SendAsync("sample.mail.v1", message, context, cancellationToken);
}

/// <summary>Publishes the durable event through the host-selected concrete client.</summary>
public sealed class OrderCreatedOutboxHandler(MemoryMessageClient client) : IOutboxHandler<OrderCreated>
{
    public async ValueTask HandleAsync(OrderCreated message, OutboxDeliveryContext context, CancellationToken cancellationToken)
        => await client.PublishAsync("sample.orders.created.v1", message, context, cancellationToken);
}
