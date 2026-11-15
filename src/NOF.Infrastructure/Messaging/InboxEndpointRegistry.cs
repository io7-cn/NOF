using NOF.Abstraction;
using NOF.Application;

namespace NOF.Infrastructure;

/// <summary>Associates an opaque durable receipt identity with its one-way application operation.</summary>
public sealed record InboxEndpoint(string Id, OperationDescriptor Operation);

/// <summary>Host-local dispatch metadata for the uniform Inbox execution base; contains no transport topology or handlers.</summary>
public sealed class InboxEndpointRegistry : Registry<InboxEndpoint>
{
    private readonly Lazy<Dictionary<string, OperationDescriptor>> _operations;

    /// <summary>Creates a registry whose lookup freezes on first execution.</summary>
    public InboxEndpointRegistry()
    {
        _operations = new(() => Freeze().ToDictionary(endpoint => endpoint.Id, endpoint => endpoint.Operation, StringComparer.Ordinal));
    }

    /// <summary>Registers a stable receipt identity; only void operations can be received without a reply.</summary>
    public override void Add(InboxEndpoint endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        ArgumentException.ThrowIfNullOrWhiteSpace(endpoint.Id);
        ArgumentNullException.ThrowIfNull(endpoint.Operation);
        if (!OperationInspector.CanSubmit(endpoint.Operation))
        {
            throw new InvalidOperationException($"Inbox operation '{endpoint.Operation.Id}' must declare void without FailureCase.");
        }
        if (Items.Any(existing => existing.Id == endpoint.Id))
        {
            throw new InvalidOperationException($"Inbox endpoint '{endpoint.Id}' has already been mapped.");
        }
        base.Add(endpoint);
    }

    /// <summary>Resolves a persisted receipt without interpreting its identifier as a queue or topic.</summary>
    public OperationDescriptor GetOperation(string id)
    {
        return _operations.Value.GetValueOrDefault(id)
            ?? throw new InvalidOperationException($"No Inbox operation for receipt '{id}'. Restore its stable mapping or migrate pending receipts before upgrading.");
    }
}
