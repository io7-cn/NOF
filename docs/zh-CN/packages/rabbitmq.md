# NOF.Infrastructure.RabbitMQ

[English](../../en/packages/rabbitmq.md)

队列操作、带确认的 JSON 发布与可选 Backplane。

AddRabbitMQ 注册原生 ConnectionFactory、惰性 RabbitMQConnection 与具体 RabbitMQMessageClient，不创建拓扑、消费者或 Outbox 存储。可重复的 ConfigureRabbitMQ 组合原生拓扑回调与 MapQueue<Operation> 声明。发布使用持久化 JSON、mandatory 路由与确认，转发 Outbox 已准备好的 Headers。消费端恢复 Context，等待操作完成后 ACK；业务负责幂等，原生配置决定重投、死信和并发。AddRabbitMQBackplane 选择独立的瞬态广播能力。

## 安装

```sh
dotnet add package NOF.Infrastructure.RabbitMQ
```

## 用法与边界

- [rabbitmq-sample](../design/rabbitmq-sample.md)
- [execution-context](../design/execution-context.md)
- [transactional-outbox](../design/transactional-outbox.md)

在原生宿主选择 Provider，不向 Contract 或 Domain 引入基础设施依赖。参见[完整 Sample](../design/sample-workbench.md)。
