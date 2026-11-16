namespace NOF.Sample.Endpoints.Models;

public sealed record DemoOutboxOverview(bool Paused, IReadOnlyList<DemoOutboxEntry> Items);
