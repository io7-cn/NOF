using NOF.Application;
using NOF.Contract;

namespace NOF.Sample.Application.Integration;

/// <summary>Compilable example of application persistence and transactional dispatch.</summary>
public static class TransactionalMessages
{

    public static async Task StageAsync(
        IUnitOfWork unitOfWork,
        IMessageDispatcher messages,
        Guid orderId,
        string email,
        Context context,
        CancellationToken cancellationToken)
    {
        // Persist business changes using this same unitOfWork before saving.
        await messages.DeferPublishAsync(new OrderCreated(orderId), context, cancellationToken);
        await messages.DeferSendAsync(IMessageDemoServiceOperations.SendMail, new SendEmail(email, "Created", "Order created."), context, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
    }
}
