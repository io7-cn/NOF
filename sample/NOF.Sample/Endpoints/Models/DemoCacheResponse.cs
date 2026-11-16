namespace NOF.Sample.Endpoints.Models;

public sealed record DemoCacheResponse(string Tenant, bool Present, string? Value);
