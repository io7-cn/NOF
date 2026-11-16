namespace NOF.Infrastructure.EntityFrameworkCore;

/// <summary>NOF-owned model policy, independent of provider configuration on the native EF Core options builder.</summary>
public sealed class NOFDbContextOptions
{
    /// <summary>Gets or sets the tenant storage mode.</summary>
    public TenantMode TenantMode { get; set; } = TenantMode.DatabasePerTenant;

    /// <summary>Gets or sets whether soft delete is enabled for entity types that do not override it.</summary>
    public bool SoftDeleteEnabled { get; set; }
}
