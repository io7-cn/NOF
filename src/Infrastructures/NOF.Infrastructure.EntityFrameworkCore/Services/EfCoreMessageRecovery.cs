using Microsoft.EntityFrameworkCore;
using NOF.Application;
using NOF.Infrastructure;

namespace NOF.Infrastructure.EntityFrameworkCore;
/// <summary>Recovers transactional messages whose processing claims have expired.</summary>

public static class EfCoreMessageRecovery
{
    private const string MaxRetryExceededError = "Exceeded max retry count";

    /// <summary>Permanently fails exhausted pending messages after their owner lease expires.</summary>
    public static Task<int> MarkExpiredExhaustedInboxMessagesAsFailedAsync(
        DbContext dbContext,
        int maxRetryCount,
        DateTime now,
        CancellationToken cancellationToken)
    {
        return dbContext.Set<NOFInboxMessage>()
            .Where(message => message.Status == InboxMessageStatus.Pending
                && message.RetryCount >= maxRetryCount
                && (message.ClaimedBy == null
                    || message.ClaimExpiresAtUtc == null
                    || message.ClaimExpiresAtUtc <= now))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(message => message.Status, InboxMessageStatus.Failed)
                .SetProperty(message => message.ErrorMessage, MaxRetryExceededError)
                .SetProperty(message => message.FailedAtUtc, now)
                .SetProperty(message => message.ClaimedBy, (string?)null)
                .SetProperty(message => message.ClaimExpiresAtUtc, (DateTime?)null),
                cancellationToken);
    }

    /// <summary>Permanently fails exhausted pending messages after their owner lease expires.</summary>
    public static Task<int> MarkExpiredExhaustedOutboxMessagesAsFailedAsync(
        DbContext dbContext,
        int maxRetryCount,
        DateTime now,
        CancellationToken cancellationToken)
    {
        return dbContext.Set<NOFOutboxMessage>()
            .Where(message => message.Status == OutboxMessageStatus.Pending
                && message.RetryCount >= maxRetryCount
                && (message.ClaimedBy == null
                    || message.ClaimExpiresAtUtc == null
                    || message.ClaimExpiresAtUtc <= now))
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(message => message.Status, OutboxMessageStatus.Failed)
                .SetProperty(message => message.ErrorMessage, MaxRetryExceededError)
                .SetProperty(message => message.FailedAtUtc, now)
                .SetProperty(message => message.ClaimedBy, (string?)null)
                .SetProperty(message => message.ClaimExpiresAtUtc, (DateTime?)null),
                cancellationToken);
    }
}
