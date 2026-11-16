using NOF.Contract;

namespace NOF.Sample;

public interface IMessageDispatchService : IApplicationService
{
    [FailureCase("InvalidRecipient", "Invalid recipient")]
    SubmittedDemoOrder SubmitOrder(SubmitDemoOrderRequest request);
}
