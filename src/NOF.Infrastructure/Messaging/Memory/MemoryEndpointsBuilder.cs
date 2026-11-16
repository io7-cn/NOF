using Microsoft.Extensions.DependencyInjection;
using NOF.Application;
using NOF.Contract;
using NOF.Infrastructure;

namespace NOF.Infrastructure;

/// <summary>Maps operations to Memory-owned queue and subscription endpoints.</summary>
public sealed class MemoryEndpointsBuilder(IServiceCollection services, MemoryEndpointRegistry registry)
{
    /// <summary>Maps a statically selected operation to a competing-consumer queue.</summary>
    public void MapQueue<TOperation>(string queue) where TOperation : OperationHandler
        => Add(queue, "memory:queue:" + queue, services.GetOrAddSingleton<ApplicationServiceRegistry>().GetOperation(typeof(TOperation)), false);

    /// <summary>Maps an explicitly typed one-way operation to a queue.</summary>
    public void MapQueue<T>(string queue, OperationDescriptor<T> operation)
        => Add(queue, "memory:queue:" + queue, operation, false);

    /// <summary>Maps a statically selected operation to an independent local topic subscription.</summary>
    public void MapSubscription<TOperation>(string topic, string subscription) where TOperation : OperationHandler
        => AddSubscription(topic, subscription, services.GetOrAddSingleton<ApplicationServiceRegistry>().GetOperation(typeof(TOperation)));

    /// <summary>Maps an explicitly typed one-way operation to a topic subscription.</summary>
    public void MapSubscription<T>(string topic, string subscription, OperationDescriptor<T> operation)
        => AddSubscription(topic, subscription, operation);

    private void AddSubscription(string topic, string subscription, OperationDescriptor operation)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subscription);
        Add(topic, "memory:subscription:" + subscription, operation, true);
    }

    private void Add(string route, string consumerId, OperationDescriptor operation, bool subscription)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(route);
        ArgumentNullException.ThrowIfNull(operation);
        if (registry.IsFrozen || services.IsReadOnly)
        {
            throw new InvalidOperationException("Configure Memory endpoints before reading bindings or starting the host.");
        }
        if (!OperationInspector.CanSubmit(operation))
        {
            throw new InvalidOperationException("Memory endpoints require a void operation without business failure cases.");
        }
        registry.Add(new(route, consumerId, operation.RequestType, operation, subscription));
    }
}
