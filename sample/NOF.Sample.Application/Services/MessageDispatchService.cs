using NOF.Application;
using NOF.Sample.Application.Integration;
using System.Net.Mail;

namespace NOF.Sample.Application;

public partial class MessageDispatchService : ApplicationService<IMessageDispatchService>;

/// <summary>Application stages transport-independent intents and saves them in one unit of work.</summary>
public sealed class SubmitDemoOrder(IUnitOfWork unitOfWork, IOutbox outbox) : MessageDispatchService.SubmitOrder
{
    public override async Task<Result> HandleAsync(SubmitDemoOrderRequest request, InvocationContext context, CancellationToken cancellationToken)
    {
        if (!MailAddress.TryCreate(request.Email, out _))
        {
            return new InvalidRecipient("Provide a valid demonstration email address.");
        }
        var orderId = Guid.NewGuid();
        await TransactionalMessages.StageAsync(unitOfWork, outbox, orderId, request.Email, context.Context, cancellationToken);
        return new SubmittedDemoOrder(orderId);
    }
}
