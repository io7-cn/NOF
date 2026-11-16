namespace NOF.Sample.Endpoints.Models;

public sealed record DemoRedeliveryResponse(Guid OrderId, string Message);
