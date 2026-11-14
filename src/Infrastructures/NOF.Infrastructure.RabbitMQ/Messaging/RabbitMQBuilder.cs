using Microsoft.Extensions.DependencyInjection;
using NOF.Application;
using NOF.Contract;
using NOF.Infrastructure;
using RabbitMQ.Client;

namespace NOF.Infrastructure.RabbitMQ;

/// <summary>Fluently configures native topology callbacks and operation consumers in independent feature modules.</summary>
public sealed class RabbitMQBuilder(IServiceCollection services, RabbitMQConfiguration configuration)
{
    /// <summary>Appends native RabbitMQ topology work to run before every consumer starts.</summary>
    public RabbitMQBuilder Topology(Func<IChannel, CancellationToken, Task> configure)
    {
        EnsureServicesMutable();
        configuration.AddTopology(configure);
        return this;
    }

    /// <summary>Maps a statically selected operation to a competing-consumer queue.</summary>
    public RabbitMQBuilder MapQueue<TOperation>(string queue, Func<IChannel, CancellationToken, Task>? configureChannel = null, bool requeueOnFailure = true) where TOperation : IServiceOperationDefinition
        => Add(queue, TOperation.Descriptor, configureChannel, requeueOnFailure);

    /// <summary>Maps an explicitly typed Empty operation to a queue.</summary>
    public RabbitMQBuilder MapQueue<T>(string queue, ServiceOperationDescriptor<T, Empty> operation, Func<IChannel, CancellationToken, Task>? configureChannel = null, bool requeueOnFailure = true)
        => Add(queue, operation, configureChannel, requeueOnFailure);

    private RabbitMQBuilder Add(string queueName, ServiceOperationDescriptor operation, Func<IChannel, CancellationToken, Task>? configureChannel, bool requeueOnFailure)
    {
        EnsureServicesMutable();
        ArgumentException.ThrowIfNullOrWhiteSpace(queueName);
        var consumerId = "rabbitmq:queue:" + queueName;
        ArgumentNullException.ThrowIfNull(operation);
        if (operation.ResponseType != typeof(Empty) || !ServiceOperationInspector.CanSubmit(operation))
        {
            throw new InvalidOperationException($"Operation '{operation.Id}' must return Empty without declared failures for asynchronous consumption.");
        }
        if (services.Any(item => item.ServiceType == typeof(IInboxHandler) && item.IsKeyedService && Equals(item.ServiceKey, consumerId)))
        {
            throw new InvalidOperationException($"Inbox endpoint '{consumerId}' has already been mapped.");
        }
        configuration.AddEndpoint(new(queueName, consumerId, operation, configureChannel, requeueOnFailure));
        services.AddInbox();
        services.AddKeyedScoped<IInboxHandler>(consumerId, (provider, _) => new OperationInboxHandler(operation,
            provider.GetRequiredService<IObjectSerializer>(), provider.GetRequiredService<IOperationExecutor>()));
        return this;
    }
    private void EnsureServicesMutable()
    {
        if (services.IsReadOnly)
        {
            throw new InvalidOperationException("Configure RabbitMQ before building the host.");
        }
    }
}
