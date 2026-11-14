namespace NOF.Infrastructure;

/// <summary>Processes an opaque durable receipt in the Inbox worker's existing scope and transaction.</summary>
/// <remarks>Transport adapters register keyed scoped handlers using the receipt's stable Route as the key.
/// The Inbox never interprets that key as a queue, subscription or transport name.</remarks>
public interface IInboxHandler
{
    /// <summary>Executes the receipt; an IFailure declines it, and technical exceptions follow Inbox retry policy.</summary>
    Task<object?> HandleAsync(ReadOnlyMemory<byte> payload, IEnumerable<KeyValuePair<string, string?>>? headers,
        CancellationToken cancellationToken);
}
