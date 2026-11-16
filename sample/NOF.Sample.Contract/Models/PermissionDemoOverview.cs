using NOF.Abstraction;
using NOF.Contract;

namespace NOF.Sample;

public sealed record PermissionDemoOverview(string? UserId, IReadOnlyList<SamplePermission> Permissions);
