using Microsoft.EntityFrameworkCore;

namespace NOF.Infrastructure.EntityFrameworkCore;

/// <summary>Applies native EF Core configuration to the host's shared model.</summary>
public interface IEFCoreModelConfiguration
{
    /// <summary>Configures entities with native EF Core APIs.</summary>
    void Configure(ModelBuilder modelBuilder);
}
