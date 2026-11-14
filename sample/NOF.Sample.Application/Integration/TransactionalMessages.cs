using NOF.Abstraction;
using NOF.Application;
using NOF.Contract;

namespace NOF.Sample.Application.Integration;

/// <summary>Compilable example of application persistence and transactional dispatch.</summary>
public static class TransactionalMessages
{

    public static async Task StageAsync(
        IUnitOfWork unitOfWork,
        IOutbox outbox,
        Guid orderId,
        string email,
        Context context,
        CancellationToken cancellationToken)
    {
        // Persist business changes using this same unitOfWork before saving.
        await outbox.EnqueueAsync(new OrderCreated(orderId), context, cancellationToken: cancellationToken);
        await outbox.EnqueueAsync(new SendEmail(email, "Created", "Order created.", OrderId: orderId), context, cancellationToken: cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
