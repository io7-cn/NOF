# Application invocation and transactional intents

Use the current [application operations guide](../en/design/unified-operations.md) and [generic transactional outbox](../en/design/transactional-outbox.md), each with its linked Chinese edition. Immediate calls use concrete clients; Application stages generic intents through IOutbox, and infrastructure owns their delivery. Legacy dispatcher and deferred command/notification APIs are removed.
