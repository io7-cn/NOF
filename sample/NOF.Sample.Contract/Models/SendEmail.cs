using NOF.Contract;

namespace NOF.Sample;

public sealed record SendEmail(string To, string Subject, string Body, Guid? OrderId = null);
