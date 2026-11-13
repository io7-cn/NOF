---
description: Add a NOF application operation or current-scope domain event handler
---

# Add an application operation

Declare one synchronous method on an IApplicationService contract. Implement its generated Service.Operation base inside the partial ApplicationService<T> container, overriding Task<TOutput> HandleAsync(TInput, InvocationContext, CancellationToken). Use invocation.Context with semantic APIs. Register the assembly and service container once; individual operations are discovered automatically.

The same operation can serve HTTP, local invocation and messaging. Host AddMessageBindings configures Queue<T>(route).To(Service.Operation.Descriptor) or Subscription<T>(topic, stableSubscriptionId).To(...). Async bindings accept only Empty outputs. Immediate producers use concrete clients; transactional intents use IOutbox.EnqueueAsync plus host AddOutbox schemas/handlers. No outbound operation routing remains. CommandHandler/NotificationHandler and their generators/registries are removed.

All service contracts need no partial, including FailureCase declarations; Result and cases belong to Application-generated operations. See docs/en/design/unified-operations.md and docs/en/design/expected-failures.md.

In-memory domain events keep InMemoryEventHandler<T> with Task HandleAsync(T, Context, CancellationToken), executing in the publisher scope. Do not turn them into reliable asynchronous consumers unless crossing that boundary is intentional.
