using NOF.Contract;

namespace NOF.Sample;

public interface IMessageDemoService : IApplicationService
{
    void SendMail(SendEmail request);
    void RecordOrder(OrderCreated notification);
    MessageReceiptsResponse GetReceipts(GetMessageReceiptsRequest request);
}
