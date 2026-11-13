using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NOF.Abstraction;
using NOF.Contract;
using NOF.Hosting;

namespace NOF.Infrastructure.EntityFrameworkCore;
/// <summary>Migrates the host database before application startup.</summary>

public sealed class DbContextMigration(Type dbContextType)
{
    internal Type DbContextType { get; } = dbContextType;

    internal async Task MigrateTenantAsync(IServiceProvider services, string tenantId, CancellationToken cancellationToken)
    {
        using var tenantContext = Context.PushCurrent(Context.Empty.WithTenantId(tenantId));
        await using var scope = services.CreateAsyncScope();
        scope.ServiceProvider.ResolveDaemonServices();
        var dbContext = (DbContext)scope.ServiceProvider.GetRequiredService(DbContextType);
        await dbContext.Database.MigrateAsync(cancellationToken);
    }
}
