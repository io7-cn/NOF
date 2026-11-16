using NOF.Abstraction;
using NOF.Contract;

namespace NOF.Sample;

public sealed record SamplePermission(string Id, string Name, string? Group = null);
