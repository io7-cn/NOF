using Microsoft.Extensions.DependencyInjection;

namespace NOF.Infrastructure;
/// <summary>Selects the host's ID generation policy.</summary>
public readonly record struct IdGeneratorSelector(IServiceCollection Services);
