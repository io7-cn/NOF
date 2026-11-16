namespace NOF.Sample.Endpoints.Models;

public sealed record DemoOutboxEntry(Guid Id, string MessageType, string Status, int RetryCount, string? ErrorMessage, DateTime CreatedAtUtc, DateTime? NextAttemptAtUtc, Guid? OrderId, System.Text.Json.JsonElement Headers);
