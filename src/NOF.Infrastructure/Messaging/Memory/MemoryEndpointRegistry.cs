using NOF.Abstraction;
using NOF.Application;
using NOF.Contract;

namespace NOF.Infrastructure;

/// <summary>Owns only Memory endpoint topology; entries freeze on first use.</summary>
public sealed class MemoryEndpointRegistry : Registry<MemoryEndpoint>
{
    /// <summary>Gets whether adapter configuration has already been read.</summary>
    public bool IsFrozen => Items.IsFrozen;

    /// <inheritdoc />
    public override void Add(MemoryEndpoint item)
    {
        if (Items.Any(existing => existing.ConsumerId == item.ConsumerId
            || !existing.IsSubscription && !item.IsSubscription && existing.Route == item.Route))
        {
            throw new InvalidOperationException($"Memory endpoint '{item.ConsumerId}' has already been mapped.");
        }
        base.Add(item);
    }

    /// <summary>Gets validated, frozen local bindings.</summary>
    public IReadOnlyList<MemoryEndpoint> Endpoints
    {
        get
        {
            var bindings = Freeze().ToArray();
            if (bindings.Any(queue => !queue.IsSubscription && bindings.Any(subscription => subscription.IsSubscription && subscription.Route == queue.Route))
                || bindings.GroupBy(binding => binding.Route, StringComparer.Ordinal).Any(group => group.Select(binding => binding.MessageType).Distinct().Count() != 1)
                || bindings.Any(binding => !OperationInspector.CanSubmit(binding.Operation) || binding.Operation.RequestType != binding.MessageType)
                || bindings.GroupBy(binding => binding.ConsumerId, StringComparer.Ordinal).Any(group => group.Count() != 1)
                || bindings.Where(binding => !binding.IsSubscription).GroupBy(binding => binding.Route, StringComparer.Ordinal).Any(group => group.Count() != 1))
            {
                throw new InvalidOperationException("Queue, subscription and target bindings must have unambiguous stable identities.");
            }
            return bindings;
        }
    }

}
