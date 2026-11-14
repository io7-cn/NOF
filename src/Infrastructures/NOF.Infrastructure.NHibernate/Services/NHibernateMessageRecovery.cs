using NHibernate.Linq;
using NOF.Application;
using NOF.Infrastructure;

namespace NOF.Infrastructure.NHibernate;
/// <summary>Recovers transactional messages whose processing claims have expired.</summary>

public static class NHibernateMessageRecovery
{
    private const string MaxRetryExceededError = "Exceeded max retry count";

    /// <summary>Permanently fails exhausted pending messages after their owner lease expires.</summary>
    public static Task<int> MarkExpiredExhaustedInboxMessagesAsFailedAsync(
        global::NHibernate.ISession dbContext,
        int maxRetryCount,
        DateTime now,
        CancellationToken cancellationToken)
    {
        return dbContext.Query<NOFInboxMessage>()
            .Where(message => message.Status == InboxMessageStatus.Pending
                && message.RetryCount >= maxRetryCount
                && (message.ClaimedBy == null
                    || message.ClaimExpiresAtUtc == null
                    || message.ClaimExpiresAtUtc <= now))
            .UpdateAsync(message => new NOFInboxMessage { Status = InboxMessageStatus.Failed, ErrorMessage = MaxRetryExceededError, FailedAtUtc = now, ClaimedBy = (string?)null, ClaimExpiresAtUtc = (DateTime?)null }, cancellationToken);
    }

    /// <summary>Permanently fails exhausted pending messages after their owner lease expires.</summary>
    public static Task<int> MarkExpiredExhaustedOutboxMessagesAsFailedAsync(
        global::NHibernate.ISession dbContext,
        int maxRetryCount,
        DateTime now,
        CancellationToken cancellationToken)
    {
        return dbContext.Query<NOFOutboxMessage>()
            .Where(message => message.Status == OutboxMessageStatus.Pending
                && message.RetryCount >= maxRetryCount
                && (message.ClaimedBy == null
                    || message.ClaimExpiresAtUtc == null
                    || message.ClaimExpiresAtUtc <= now))
            .UpdateAsync(message => new NOFOutboxMessage { Status = OutboxMessageStatus.Failed, ErrorMessage = MaxRetryExceededError, FailedAtUtc = now, ClaimedBy = (string?)null, ClaimExpiresAtUtc = (DateTime?)null }, cancellationToken);
    }
}
