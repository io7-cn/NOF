using Microsoft.Extensions.DependencyInjection;

namespace NOF.Infrastructure;
/// <summary>Selects the object storage provider after common tenant-prefix policy is registered.</summary>
public readonly record struct ObjectStorageSelector(IServiceCollection Services);
