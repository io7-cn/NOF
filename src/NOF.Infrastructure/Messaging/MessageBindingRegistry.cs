using Microsoft.Extensions.DependencyInjection;
using NOF.Abstraction;
using NOF.Application;
using NOF.Contract;

namespace NOF.Infrastructure;

/// <summary>A durable host binding from a queue or subscription to one Empty operation.</summary>
public sealed record MessageBinding(string Route, string ConsumerId, Type MessageType,
    ServiceOperationDescriptor Operation, bool IsSubscription);

/// <summary>Owns inbound host binding metadata; all registrations freeze on first use.</summary>
public sealed class MessageBindingRegistry : Registry<MessageBinding>
{
    /// <summary>Gets validated, frozen durable bindings.</summary>
    public IReadOnlyList<MessageBinding> Bindings
    {
        get
        {
            var bindings = Freeze().ToArray();
            if (bindings.Any(queue => !queue.IsSubscription && bindings.Any(subscription => subscription.IsSubscription && subscription.Route == queue.Route))
                || bindings.GroupBy(binding => binding.Route, StringComparer.Ordinal).Any(group => group.Select(binding => binding.MessageType).Distinct().Count() != 1)
                || bindings.Any(binding => !ServiceOperationInspector.CanSubmit(binding.Operation) || binding.Operation.ResponseType != typeof(Empty) || binding.Operation.RequestType != binding.MessageType)
                || bindings.GroupBy(binding => binding.ConsumerId, StringComparer.Ordinal).Any(group => group.Count() != 1)
                || bindings.Where(binding => !binding.IsSubscription).GroupBy(binding => binding.Route, StringComparer.Ordinal).Any(group => group.Count() != 1))
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
        ArgumentException.ThrowIfNullOrWhiteSpace(topic);
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
        registry.Add(new(route, consumerId, typeof(T), operation, isSubscription));
    }
}

/// <summary>Registers inbound message bindings owned by the host.</summary>
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
