using NOF.Contract;

namespace NOF.Sample;

public sealed record SubmitDemoOrderRequest(string Email);
public sealed record SubmittedDemoOrder(Guid OrderId);
