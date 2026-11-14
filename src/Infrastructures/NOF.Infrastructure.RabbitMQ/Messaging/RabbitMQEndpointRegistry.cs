using NOF.Abstraction;
using NOF.Application;
using NOF.Contract;

namespace NOF.Infrastructure.RabbitMQ;

/// <summary>One RabbitMQ adapter endpoint targeting an Empty operation.</summary>
public sealed record RabbitMQEndpoint(string Route, string ConsumerId, string QueueName, Type MessageType,
    ServiceOperationDescriptor Operation, bool IsSubscription);

/// <summary>Owns only RabbitMQ endpoint topology; entries freeze on first use.</summary>
public sealed class RabbitMQEndpointRegistry : Registry<RabbitMQEndpoint>
{
    /// <summary>Gets validated, frozen durable bindings.</summary>
    public IReadOnlyList<RabbitMQEndpoint> Endpoints
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
    public RabbitMQEndpoint GetConsumer(string consumerId)
        => Endpoints.SingleOrDefault(binding => binding.ConsumerId == consumerId)
            ?? throw new InvalidOperationException($"No message binding for '{consumerId}'. Drain or migrate old inbox records before upgrading.");
}
