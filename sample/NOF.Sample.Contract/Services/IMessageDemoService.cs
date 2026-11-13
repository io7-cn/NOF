using NOF.Contract;

namespace NOF.Sample;

public sealed record SendEmail(string To, string Subject, string Body);
public sealed record OrderCreated(Guid OrderId);

public interface IMessageDemoService : IApplicationService
{
    Empty SendMail(SendEmail request);
    Empty RecordOrder(OrderCreated notification);
}
