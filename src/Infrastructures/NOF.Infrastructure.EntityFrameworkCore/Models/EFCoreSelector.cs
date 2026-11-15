using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace NOF.Infrastructure.EntityFrameworkCore;

public readonly struct EFCoreSelector
{
    public IHostApplicationBuilder Builder { get; }
    public Type DbContextType { get; }

    public EFCoreSelector(IHostApplicationBuilder builder, Type dbContextType)
    {
        Builder = builder;
        DbContextType = dbContextType;
    }

    /// <summary>Configures NOF-owned tenant and soft-delete policy; provider settings use the native EF builder callback.</summary>
    public EFCoreSelector ConfigureNOFDbContextOptions(Action<NOFDbContextOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        Builder.Services.Configure(configure);
        return this;
    }

    /// <summary>
    /// Waits for host database migration before hosted services start, then migrates tenant databases
    /// supplied by <see cref="ITenantProvider"/> in the background after the application starts.
    /// </summary>
    public EFCoreSelector MigrateOnStart()
    {
        var dbContextType = DbContextType;
        if (!Builder.Services.Any(descriptor => descriptor.ImplementationInstance is DbContextMigration migration
            && migration.DbContextType == dbContextType))
        {
            Builder.Services.AddSingleton(new DbContextMigration(dbContextType));
        }
        Builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, DatabaseMigrationHostedService>());
        Builder.Services.TryAddSingleton<ITenantProvider, HostTenantProvider>();
        Builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, TenantDatabaseMigrationBackgroundService>());
        return this;
    }
}
