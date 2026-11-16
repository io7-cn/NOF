namespace NOF.Sample.Endpoints.Models;

public sealed record DemoValueResponse(bool Valid, string? Value = null, string? Error = null);
