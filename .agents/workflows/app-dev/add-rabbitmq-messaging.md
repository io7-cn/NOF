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

`AddRabbitMQ(...)` configures options, registers concrete RabbitMQMessageClient and its consumer hosted service. It does not replace MemoryMessageClient.

## 3. Configure the Connection

`RabbitMQOptions.ConnectionString` accepts either an AMQP URI or key/value form:

```json
{
  "ConnectionStrings": {
    "rabbitmq": "Host=localhost;Port=5672;UserName=guest;Password=guest;VirtualHost=/"
  }
}
```

The options also expose durability, auto-delete, prefetch, requeue, and publisher-confirmation settings.

## 4. Use the Concrete Client or Generic Outbox

```csharp
await rabbitClient.SendAsync("mail.send.v1", command, context, cancellationToken);
await rabbitClient.PublishAsync("orders.created.v1", notification, context, cancellationToken);
await outbox.EnqueueAsync(notification, context, cancellationToken: cancellationToken);
await unitOfWork.SaveChangesAsync(cancellationToken);
```

The host registers AddOutbox stable schemas and IOutboxHandler<T> adapters using RabbitMQMessageClient. Use its OutboxDeliveryContext overload to retain message ID and ordering. Inbound Queue/Subscription bindings are independent. See docs/en/design/transactional-outbox.md.

## 5. Optional RabbitMQ Backplane

`builder.Services.AddRabbitMQBackplane(...)` replaces `IBackplane` only. It is independent from `AddRabbitMQ(...)`; enable it when application backplane traffic should also use RabbitMQ.
