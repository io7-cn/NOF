using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NOF.Hosting;
using NOF.Infrastructure.EntityFrameworkCore;

namespace NOF.Infrastructure.Tests;

public static class NativePersistenceTestSetup
{
    public static void AddNativeTestPersistence(this IHostApplicationBuilder builder)
    {
        builder.UseDbContext<NOFDbContext>()
            .WithConnectionString($"Data Source=nof-test-{Guid.NewGuid():N};Mode=Memory;Cache=Shared")
            .WithOptions((options, connection) => options.UseSqlite(connection));
    }
}
