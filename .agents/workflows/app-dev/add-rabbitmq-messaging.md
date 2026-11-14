---
description: Add an explicit RabbitMQ client and inbound message consumers
---

# Add RabbitMQ Messaging

## 1. Add the Package to the Host

```bash
dotnet add package NOF.Infrastructure.RabbitMQ
```

## 2. Register RabbitMQ

```csharp
builder.Services.AddRabbitMQ(options =>
{
    options.ConnectionString = builder.Configuration.GetConnectionString("rabbitmq");
});
```

`AddRabbitMQ(options)` registers clients only. To receive, pass an optional adapter-owned endpoint callback:

```csharp
builder.Services.AddRabbitMQ(options => options.ConnectionString = connectionString, endpoints =>
{
    endpoints.MapQueue<MailService.Send>("mail.send.v1");
    endpoints.MapQueue<OrderAudit.Record>("orders.audit.v1");
});
```

The callback enables Inbox and RabbitMQ consumers. It does not replace MemoryMessageClient or consume Memory endpoints. Core Inbox resolves scoped keyed IInboxHandler entries without understanding topology. See docs/en/design/rabbitmq-sample.md for the complete two-service demo and explicit old receipt/order-state key migration.

## 3. Configure the Connection

`RabbitMQOptions.ConnectionString` accepts either an AMQP URI or key/value form:

```json
{
  "ConnectionStrings": {
    "rabbitmq": "Host=localhost;Port=5672;UserName=guest;Password=guest;VirtualHost=/"
  }
}
```

Connection options also expose consumer prefetch and requeue behavior. Topology belongs to native RabbitMQ setup, and publisher confirms use native CreateChannelOptions.

## 4. Use the Concrete Client or Generic Outbox

```csharp
await channel.BasicPublishAsync(exchange, routingKey, mandatory: true, properties, payload, cancellationToken);
await outbox.EnqueueAsync(notification, context, cancellationToken: cancellationToken);
await unitOfWork.SaveChangesAsync(cancellationToken);
```

The host registers AddOutbox stable schemas and IOutboxHandler<T> adapters using native IChannel.BasicPublishAsync. Wrap dispatch with MessageDelivery.ExecuteAsync(..., deliveryContext) to preserve message ID and ordering; configure publisher confirms through native CreateChannelOptions. Inbound RabbitMQ endpoints are adapter-owned and independent from Memory endpoints. See docs/en/design/transactional-outbox.md.

## 5. Optional RabbitMQ Backplane

`builder.Services.AddRabbitMQBackplane(...)` replaces `IBackplane` only. It is independent from `AddRabbitMQ(...)`; enable it when application backplane traffic should also use RabbitMQ.
