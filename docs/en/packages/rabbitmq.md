# NOF.Infrastructure.RabbitMQ

[简体中文](../../zh-CN/packages/rabbitmq.md)

Queue operations, confirmed JSON publishing and optional backplane.

AddRabbitMQ registers the native ConnectionFactory, lazy RabbitMQConnection and concrete RabbitMQMessageClient; it creates no topology, consumer or Outbox store. Repeatable ConfigureRabbitMQ composes native topology callbacks and MapQueue<Operation> declarations. The publisher uses persistent JSON, mandatory routing and publisher confirmations; it forwards ready-to-send Outbox headers. Consumers restore Context and ACK after awaited operation completion. Business owns idempotency; native settings own requeue/dead-letter and concurrency. AddRabbitMQBackplane selects the separate transient broadcast capability.

## Installation

```sh
dotnet add package NOF.Infrastructure.RabbitMQ
```

## Usage and boundaries

- [rabbitmq-sample](../design/rabbitmq-sample.md)
- [execution-context](../design/execution-context.md)
- [transactional-outbox](../design/transactional-outbox.md)

Select providers in the native host; do not add infrastructure dependencies to Contract or Domain. See [the complete Sample](../design/sample-workbench.md).
