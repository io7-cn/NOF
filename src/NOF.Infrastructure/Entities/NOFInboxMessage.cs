using System.ComponentModel;

namespace NOF.Infrastructure;

public class NOFInboxMessage : IEquatable<NOFInboxMessage>
{
    public Guid Id { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public int RetryCount { get; set; }

    public string Route { get; set; } = null!;
    public byte[] Payload { get; set; } = null!;
    public string Headers { get; set; } = null!;
    public DateTime? ProcessedAtUtc { get; set; }
    public DateTime? FailedAtUtc { get; set; }
    public string? ErrorMessage { get; set; }

    public string? ClaimedBy { get; set; }
    public DateTime? ClaimExpiresAtUtc { get; set; }
    public InboxMessageStatus Status { get; set; } = InboxMessageStatus.Pending;

    /// <summary>Compares durable receipt identity, including the adapter-owned route.</summary>
    public bool Equals(NOFInboxMessage? other) => other is not null && Id == other.Id && string.Equals(Route, other.Route, StringComparison.Ordinal);
    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is NOFInboxMessage other && Equals(other);
    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(Id, Route);

    [EditorBrowsable(EditorBrowsableState.Never)]
    public static NOFInboxMessage Create(
        Guid id,
        string route,
        byte[] payload,
        string headers)
        => new()
        {
            Id = id,
            Route = route,
            Payload = payload,
            Headers = headers
        };
}

public enum InboxMessageStatus
{
    Pending = 0,
    Processed = 1,
    Failed = 2
}
