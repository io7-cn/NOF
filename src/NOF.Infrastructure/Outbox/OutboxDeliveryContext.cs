using NOF.Abstraction;

namespace NOF.Infrastructure;

/// <summary>Trusted metadata for one delivery attempt; it contains no replayed user identity.</summary>
public sealed record OutboxDeliveryContext
{
    /// <summary>Gets the stable idempotency key shared by every retry.</summary>
    public required Guid MessageId { get; init; }
    /// <summary>Gets the persisted tenant and explicitly supported semantic metadata.</summary>
    public required Context Context { get; init; }
    /// <summary>Gets the one-based attempt number.</summary>
    public required int Attempt { get; init; }
    /// <summary>Gets the qualified ordering stream.</summary>
    public string? OrderKey { get; init; }
    /// <summary>Gets the sequence in the ordering stream.</summary>
    public long? Sequence { get; init; }
    /// <summary>Gets whether this intent completes its stream.</summary>
    public bool CompletesOrderKey { get; init; }
}
