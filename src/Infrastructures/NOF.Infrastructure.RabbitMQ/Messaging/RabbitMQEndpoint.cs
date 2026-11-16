using Microsoft.Extensions.DependencyInjection;
using NOF.Application;
using NOF.Contract;
using NOF.Infrastructure;
using RabbitMQ.Client;

namespace NOF.Infrastructure.RabbitMQ;

/// <summary>One queue consumer and its native channel settings.</summary>
public sealed record RabbitMQEndpoint(string QueueName, OperationDescriptor Operation,
    Func<IChannel, CancellationToken, Task>? ConfigureChannel = null, bool RequeueOnFailure = true);
