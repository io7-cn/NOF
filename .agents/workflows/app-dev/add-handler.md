---
description: Add a NOF application operation or current-scope domain event handler
---

# Add an application operation

Declare one synchronous method on an IApplicationService contract. Implement its generated Service.Operation base inside the partial ApplicationService<T> container, overriding Task<TOutput> HandleAsync(TInput, InvocationContext, CancellationToken). Use invocation.Context with semantic APIs. Register the assembly and service container once; individual operations are discovered automatically.

Messaging: endpoints directly execute the mapped OperationHandler in isolated scopes through OperationExecutor, restoring Context before constructors and preserving authorization/filters. No Inbox/store/entity/receipt registry or consumer worker ships. Business owns idempotency, saves and transactions; native adapters own ACK/NACK, retry/dead-letter and concurrency. RabbitMQ ACK follows completed execution, not acceptance. Memory awaits handlers and propagates failures without process durability. MapQueue/AddMemoryMessaging never enable persistence. AddOutbox explicitly enables producer staging/workers; configure flat OutboxOptions and retain generated OutboxHandler<T> discovery. Resolve legacy Inbox Pending/Failed work with old binaries or explicit reviewed export/replay before guarded RemoveInbox; preserve required historical receipts via backup. See docs/en/design/transactional-outbox.md.

All service contracts need no partial, including FailureCase declarations; Result and cases belong to Application-generated operations. See docs/en/design/unified-operations.md and docs/en/design/expected-failures.md.

In-memory domain events keep LocalEventHandler<T> with Task HandleAsync(T, Context, CancellationToken), executing in the publisher scope. Do not turn them into reliable asynchronous consumers unless crossing that boundary is intentional.
