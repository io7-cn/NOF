using System.ComponentModel;

namespace NOF.Infrastructure;

/// <summary>
/// Outbox message entity used for adding messages in transactional context and reading by background services.
/// </summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public class NOFOutboxMessage
{
    public Guid Id { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public int RetryCount { get; set; }

    public string MessageType { get; set; } = null!;
    public DateTime? NextAttemptAtUtc { get; set; }
    public byte[] Payload { get; set; } = null!;
    public string Headers { get; set; } = null!;
    public DateTime? SentAtUtc { get; set; }
    public DateTime? FailedAtUtc { get; set; }
    public string? ErrorMessage { get; set; }

    public string? ClaimedBy { get; set; }
    public DateTime? ClaimExpiresAtUtc { get; set; }
    public OutboxMessageStatus Status { get; set; } = OutboxMessageStatus.Pending;

    public string? TraceParent { get; set; }

    internal static NOFOutboxMessage Create(
        string messageType,
        byte[] payload,
        string headers,
        string? traceParent)
    {
        var createdAtUtc = DateTime.UtcNow;
        return new NOFOutboxMessage
        {
            Id = Guid.CreateVersion7(new DateTimeOffset(createdAtUtc)),
            CreatedAtUtc = createdAtUtc,
            MessageType = messageType,
            Payload = payload,
            Headers = headers,
            TraceParent = traceParent
        };
    }
}
