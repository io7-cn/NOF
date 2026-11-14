---
description: Add a NOF application operation or current-scope domain event handler
---

# Add an application operation

Declare one synchronous method on an IApplicationService contract. Implement its generated Service.Operation base inside the partial ApplicationService<T> container, overriding Task<TOutput> HandleAsync(TInput, InvocationContext, CancellationToken). Use invocation.Context with semantic APIs. Register the assembly and service container once; individual operations are discovered automatically.

The same operation can serve HTTP, local invocation and messaging. The RabbitMQ adapter configures MapQueue<Service.Operation>(queue) or MapSubscription<Service.Operation>(topic, stableSubscriptionId) via AddRabbitMQ(options, endpoints); Memory configures its independent endpoints through AddMemoryMessaging. Async endpoints require Empty with no declared failures. Core Inbox resolves keyed IInboxHandler entries; it owns no shared MessageBinding registry. Immediate producers use concrete clients; transactional intents use IOutbox.EnqueueAsync plus host AddOutbox schemas/handlers. No outbound operation routing remains. CommandHandler/NotificationHandler and their generators/registries are removed.

All service contracts need no partial, including FailureCase declarations; Result and cases belong to Application-generated operations. See docs/en/design/unified-operations.md and docs/en/design/expected-failures.md.

In-memory domain events keep LocalEventHandler<T> with Task HandleAsync(T, Context, CancellationToken), executing in the publisher scope. Do not turn them into reliable asynchronous consumers unless crossing that boundary is intentional.
