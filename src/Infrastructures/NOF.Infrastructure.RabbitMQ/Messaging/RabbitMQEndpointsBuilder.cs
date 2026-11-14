using Microsoft.Extensions.DependencyInjection;
using NOF.Application;
using NOF.Contract;
using NOF.Infrastructure;

namespace NOF.Infrastructure.RabbitMQ;

/// <summary>Maps operations to RabbitMQ-owned queue and subscription endpoints.</summary>
public sealed class RabbitMQEndpointsBuilder(IServiceCollection services, RabbitMQEndpointRegistry registry)
{
    /// <summary>Maps a statically selected operation to a competing-consumer queue.</summary>
    public void MapQueue<TOperation>(string queue) where TOperation : IServiceOperationDefinition
        => Add(queue, "rabbitmq:queue:" + queue, TOperation.Descriptor, false, queue);

    /// <summary>Maps an explicitly typed Empty operation to a queue.</summary>
    public void MapQueue<T>(string queue, ServiceOperationDescriptor<T, Empty> operation)
        => Add(queue, "rabbitmq:queue:" + queue, operation, false, queue);

    /// <summary>Maps a statically selected operation to an independent durable topic subscription.</summary>
    public void MapSubscription<TOperation>(string topic, string subscription) where TOperation : IServiceOperationDefinition
        => AddSubscription(topic, subscription, TOperation.Descriptor);

    /// <summary>Maps an explicitly typed Empty operation to a topic subscription.</summary>
    public void MapSubscription<T>(string topic, string subscription, ServiceOperationDescriptor<T, Empty> operation)
        => AddSubscription(topic, subscription, operation);

    private void AddSubscription(string topic, string subscription, ServiceOperationDescriptor operation)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subscription);
        Add(topic, "rabbitmq:subscription:" + subscription, operation, true, "subscription:" + subscription);
    }

    private void Add(string route, string consumerId, ServiceOperationDescriptor operation, bool subscription, string queueName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(route);
        ArgumentNullException.ThrowIfNull(operation);
        if (operation.ResponseType != typeof(Empty) || !ServiceOperationInspector.CanSubmit(operation))
        {
            throw new InvalidOperationException($"Operation '{operation.Id}' must return Empty without declared failures for asynchronous consumption.");
        }
        if (services.Any(item => item.ServiceType == typeof(IInboxHandler) && item.IsKeyedService && Equals(item.ServiceKey, consumerId)))
        {
            throw new InvalidOperationException($"Inbox endpoint '{consumerId}' has already been mapped.");
        }
        registry.Add(new(route, consumerId, queueName, operation.RequestType, operation, subscription));
        services.AddKeyedScoped<IInboxHandler>(consumerId, (provider, _) => new OperationInboxHandler(operation,
            provider.GetRequiredService<IObjectSerializer>(), provider.GetRequiredService<IOperationExecutor>()));
    }
}
