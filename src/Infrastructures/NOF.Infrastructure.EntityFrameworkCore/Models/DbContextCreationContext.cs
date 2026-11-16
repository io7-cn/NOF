namespace NOF.Infrastructure.EntityFrameworkCore;

/// <summary>Supplies the scoped services and explicit normalized tenant to native EF options configuration.</summary>
/// <param name="Services">The current factory's scoped service provider.</param>
/// <param name="DbContextType">The concrete context being created.</param>
/// <param name="TenantId">The normalized explicitly selected tenant, independent of the caller's ambient context.</param>
/// <param name="TenantMode">The NOF tenant storage policy for this creation.</param>
public sealed record DbContextCreationContext(IServiceProvider Services, Type DbContextType, string TenantId, TenantMode TenantMode);
