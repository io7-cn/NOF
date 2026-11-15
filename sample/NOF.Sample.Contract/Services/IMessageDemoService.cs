using NOF.Contract;

namespace NOF.Sample;

public sealed record SendEmail(string To, string Subject, string Body, Guid? OrderId = null);
public sealed record OrderCreated(Guid OrderId);

public interface IMessageDemoService : IApplicationService
{
    void SendMail(SendEmail request);
    void RecordOrder(OrderCreated notification);
    MessageReceiptsResponse GetReceipts(GetMessageReceiptsRequest request);
}
