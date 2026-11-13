---
description: Add in-process event handlers and transactional outbox dispatch to a NOF application
---

# Add In-Process Events and Transactional Messages

NOF has two separate mechanisms:

- in-process, current-scope events through `IEventPublisher` and `LocalEventHandler<T>`
- cross-boundary commands/notifications, optionally staged in the transactional outbox

## 1. Define and Handle an In-Process Event

```csharp
public sealed record ProjectionRebuilt(string TenantId);

public sealed class ProjectionRebuiltHandler : LocalEventHandler<ProjectionRebuilt>
{
    public override Task HandleAsync(
        ProjectionRebuilt @event,
        Context context,
        CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
```

## 2. Publish Explicitly

```csharp
await eventPublisher.PublishAsync(
    new ProjectionRebuilt("tenant-a"),
    context,
    cancellationToken);
```

Domain methods may instead call `payload.PublishAsEvent()`. At a NOF handler/event boundary, the ambient publisher carries the current `Context` into nested in-memory events.

## 3. Stage a Transactional Intent

```csharp
await outbox.EnqueueAsync(new OrderCreatedNotification(order.Id), invocation.Context,
    cancellationToken: cancellationToken);
await unitOfWork.SaveChangesAsync(cancellationToken);
```

Inject IOutbox in the Application operation. The host registers its stable ID/version and an IOutboxHandler<T> that calls a concrete client. Enqueue snapshots without sending or saving. Use OutboxEnqueueOptions for an ordered stream and complete only its final intent. See docs/en/design/transactional-outbox.md.
