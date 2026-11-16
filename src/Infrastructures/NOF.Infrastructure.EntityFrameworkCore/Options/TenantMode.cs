namespace NOF.Infrastructure.EntityFrameworkCore;

/// <summary>Defines how tenant data is separated by the persistence provider.</summary>
public enum TenantMode
{
    /// <summary>Stores tenants in one database and applies tenant-aware model filters.</summary>
    SharedDatabase = 0,
    /// <summary>Resolves a database connection independently for each tenant.</summary>
    DatabasePerTenant = 1
}
