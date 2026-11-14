using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using NOF.Infrastructure.EntityFrameworkCore;

namespace NOF.Sample;

/// <summary>Builds the production model for migration scaffolding without connecting to a database.</summary>
public sealed class ConfigurationDbContextDesignTimeFactory : IDesignTimeDbContextFactory<ConfigurationDbContext>
{
    public ConfigurationDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<ConfigurationDbContext>()
            .UseNpgsql("Host=localhost;Database=nof_sample_design;Username=postgres")
            .ReplaceService<IModelCustomizer, NOFModelCustomizer>()
            .ReplaceService<IValueConverterSelector, ValueObjectValueConverterSelector>();
        ((IDbContextOptionsBuilderInfrastructure)options).AddOrUpdateExtension(new NOFTenantDbContextOptionsExtension
        {
            TenantId = "host",
            TenantMode = TenantMode.DatabasePerTenant,
            SoftDeleteEnabled = true
        });
        ((IDbContextOptionsBuilderInfrastructure)options).AddOrUpdateExtension(new NOFModelCreatingDbContextOptionsExtension
        {
            Contributors = [new NOFInboxMessageModelCreatingContributor(), new NOFOutboxMessageModelCreatingContributor()]
        });
        return new(options.Options);
    }
}
