using Microsoft.Extensions.DependencyInjection;
using NOF.Abstraction;
using NOF.Application;
using NOF.Contract;

namespace NOF.Infrastructure;

/// <summary>A durable host binding from a queue or subscription to one Empty operation.</summary>
public sealed record MessageBinding(string Route, string ConsumerId, Type MessageType,
    ServiceOperationDescriptor Operation, bool IsSubscription);

/// <summary>Owns host routing metadata; all registrations freeze on first use.</summary>
public sealed class MessageBindingRegistry : Registry<MessageBinding>
{
    private readonly Dictionary<Type, string> _topics = [];
    private readonly Dictionary<string, (Type Input, string Route)> _queues = new(StringComparer.Ordinal);

    /// <summary>Declares an outbound queue route without installing a local consumer.</summary>
    public void AddQueueRoute(ServiceOperationReference operation, string queue)
    {
        ArgumentNullException.ThrowIfNull(operation);
        ArgumentException.ThrowIfNullOrWhiteSpace(queue);
        if (!ServiceOperationInspector.CanSubmit(operation))
        {
            throw new InvalidOperationException($"Operation '{operation.Id}' declares a response or expected failures and cannot be submitted as fire-and-forget work.");
        }
        if (Items.IsFrozen)
        {
            throw new InvalidOperationException("Message bindings are frozen.");
        }
        var target = (operation.RequestType, queue);
        if (_queues.TryGetValue(operation.Id, out var existing) && existing != target)
        {
            throw new InvalidOperationException($"Conflicting queues for '{operation.Id}'.");
        }
        _queues[operation.Id] = target;
    }

    /// <summary>Registers a stable publication route without referencing any consumer.</summary>
    public void AddTopic(Type messageType, string route)
    {
        ArgumentNullException.ThrowIfNull(messageType);
        ArgumentException.ThrowIfNullOrWhiteSpace(route);
        if (Items.IsFrozen)
        {
            throw new InvalidOperationException("Message bindings are frozen.");
        }
        if (_topics.TryGetValue(messageType, out var existing) && existing != route)
        {
            throw new InvalidOperationException($"Conflicting topics for '{messageType}'.");
        }
        if (_topics.Any(pair => pair.Key != messageType && pair.Value == route))
        {
            throw new InvalidOperationException($"Topic '{route}' already has a different payload type.");
        }
        _topics[messageType] = route;
    }

    /// <summary>Resolves a stable publication route, never a CLR payload name.</summary>
    public string GetTopic(Type messageType)
    {
        _ = Bindings;
        return _topics.TryGetValue(messageType, out var topic) ? topic
            : throw new InvalidOperationException($"No topic is configured for '{messageType}'.");
    }

    /// <summary>Resolves the single configured queue for a selected target operation.</summary>
    public string GetQueue(ServiceOperationReference operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        _ = Bindings;
        return _queues.TryGetValue(operation.Id, out var queue) && queue.Input == operation.RequestType ? queue.Route
            : throw new InvalidOperationException($"No queue is configured for '{operation.Id}'.");
    }

    /// <summary>Gets validated, frozen durable bindings.</summary>
    public IReadOnlyList<MessageBinding> Bindings
    {
        get
        {
            var bindings = Freeze().ToArray();
            if (_queues.Values.Any(queue => _topics.Values.Contains(queue.Route, StringComparer.Ordinal))
                || bindings.Any(binding => !ServiceOperationInspector.CanSubmit(binding.Operation) || binding.Operation.ResponseType != typeof(Empty) || binding.Operation.RequestType != binding.MessageType)
                || bindings.GroupBy(binding => binding.ConsumerId, StringComparer.Ordinal).Any(group => group.Count() != 1)
                || bindings.Where(binding => !binding.IsSubscription).GroupBy(binding => binding.Route, StringComparer.Ordinal).Any(group => group.Count() != 1)
                || bindings.Where(binding => !binding.IsSubscription).GroupBy(binding => binding.Operation.Id, StringComparer.Ordinal).Any(group => group.Count() != 1))
            {
                throw new InvalidOperationException("Queue, subscription and target bindings must have unambiguous stable identities.");
            }
            return bindings;
        }
    }

    /// <summary>Resolves one durable consumer independently of its current implementation type.</summary>
    public MessageBinding GetConsumer(string consumerId)
        => Bindings.SingleOrDefault(binding => binding.ConsumerId == consumerId)
            ?? throw new InvalidOperationException($"No message binding for '{consumerId}'. Drain or migrate old inbox records before upgrading.");
}

/// <summary>Declares transport bindings in the host composition root.</summary>
public sealed class MessageBindingsBuilder(MessageBindingRegistry registry)
{
    /// <summary>Routes submitted work to a remote queue without creating a local consumer.</summary>
    public MessageBindingsBuilder RouteToQueue<T>(ServiceOperationReference<T> operation, string queue)
    {
        registry.AddQueueRoute(operation, queue);
        return this;
    }
    /// <summary>Defines the stable event route used by publication.</summary>
    public MessageBindingsBuilder Topic<T>(string topic)
    {
        registry.AddTopic(typeof(T), topic);
        return this;
    }
    /// <summary>Declares a queue whose instances compete for delivery.</summary>
    public MessageBindingTarget<T> Queue<T>(string queue)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(queue);
        return new(registry, queue, "queue:" + queue, false);
    }
    /// <summary>Declares a subscription with independent deduplication, ordering and retry progress.</summary>
    public MessageBindingTarget<T> Subscription<T>(string topic, string subscription)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subscription);
        registry.AddTopic(typeof(T), topic);
        return new(registry, topic, "subscription:" + subscription, true);
    }
}

/// <summary>Restricts asynchronous consumers to operations with no discarded business output.</summary>
public sealed class MessageBindingTarget<T>(MessageBindingRegistry registry, string route, string consumerId, bool isSubscription)
{
    /// <summary>Binds this input to one operation whose declared output is Empty.</summary>
    public void To(ServiceOperationDescriptor<T, Empty> operation)
    {
        ArgumentNullException.ThrowIfNull(operation);
        if (!ServiceOperationInspector.CanSubmit(operation))
        {
            throw new InvalidOperationException($"Operation '{operation.Id}' declares a response or expected failures and cannot discard its output.");
        }
        if (!isSubscription)
        {
            registry.AddQueueRoute(operation, route);
        }
        registry.Add(new(route, consumerId, typeof(T), operation, isSubscription));
    }
}

/// <summary>Registers message routing owned by the host.</summary>
public static class MessageBindingExtensions
{
    /// <summary>Configures queues, event topics and stable subscriptions.</summary>
    public static IServiceCollection AddMessageBindings(this IServiceCollection services, Action<MessageBindingsBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        configure(new(services.GetOrAddSingleton<MessageBindingRegistry>()));
        return services;
    }
}
