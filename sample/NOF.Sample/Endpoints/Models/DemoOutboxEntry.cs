using NOF.Infrastructure;
using System.Text.Json.Serialization;

namespace NOF.Sample.Endpoints.Models;

public sealed record DemoOutboxEntry(Guid Id, string MessageType,
    [property: JsonConverter(typeof(JsonStringEnumConverter<OutboxMessageStatus>))] OutboxMessageStatus Status,
    int RetryCount, string? ErrorMessage, DateTime CreatedAtUtc, DateTime? NextAttemptAtUtc,
    Guid? OrderId, IReadOnlyDictionary<string, string?> Headers);
