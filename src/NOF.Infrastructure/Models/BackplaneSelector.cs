using Microsoft.Extensions.DependencyInjection;

namespace NOF.Infrastructure;
/// <summary>Selects a transient broadcast provider.</summary>
public readonly record struct BackplaneSelector(IServiceCollection Services);
