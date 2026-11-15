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
        var connection = $"Data Source=nof-test-{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
        builder.UseDbContext<NOFDbContext>((_, ef) => ef.UseSqlite(connection));
    }
}
