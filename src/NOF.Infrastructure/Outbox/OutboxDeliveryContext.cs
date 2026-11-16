using NOF.Abstraction;

namespace NOF.Infrastructure;

/// <summary>Trusted metadata for one delivery attempt; it contains no replayed user identity.</summary>
public sealed record OutboxDeliveryContext
{
    /// <summary>Gets the stable idempotency key shared by every retry.</summary>
    public required Guid MessageId { get; init; }
    /// <summary>Gets the complete persisted JSON-scalar execution context.</summary>
    public required Context Context { get; init; }
    /// <summary>Gets the one-based attempt number.</summary>
    public required int Attempt { get; init; }
    /// <summary>Gets ready-to-send headers, including the stable message ID, tenant, trace and serialized context.</summary>
    public required IReadOnlyDictionary<string, string?> Headers { get; init; }
}
