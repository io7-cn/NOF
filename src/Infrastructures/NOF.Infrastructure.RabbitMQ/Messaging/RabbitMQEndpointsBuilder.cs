using Microsoft.Extensions.DependencyInjection;
using NOF.Application;
using NOF.Contract;
using NOF.Infrastructure;

namespace NOF.Infrastructure.RabbitMQ;

/// <summary>Maps operations to RabbitMQ-owned existing queues.</summary>
public sealed class RabbitMQEndpointsBuilder(IServiceCollection services, RabbitMQEndpointRegistry registry)
{
    /// <summary>Maps a statically selected operation to a competing-consumer queue.</summary>
    public void MapQueue<TOperation>(string queue) where TOperation : IServiceOperationDefinition
        => Add(queue, TOperation.Descriptor);

    /// <summary>Maps an explicitly typed Empty operation to a queue.</summary>
    public void MapQueue<T>(string queue, ServiceOperationDescriptor<T, Empty> operation)
        => Add(queue, operation);

    private void Add(string queueName, ServiceOperationDescriptor operation)
    {
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
        registry.Add(new(queueName, consumerId, operation));
        services.AddKeyedScoped<IInboxHandler>(consumerId, (provider, _) => new OperationInboxHandler(operation,
            provider.GetRequiredService<IObjectSerializer>(), provider.GetRequiredService<IOperationExecutor>()));
    }
}
