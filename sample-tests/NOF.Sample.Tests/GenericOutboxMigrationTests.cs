using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using NOF.Sample.Migrations;
using Xunit;

namespace NOF.Sample.Tests;

public sealed class GenericOutboxMigrationTests
{
    [Fact]
    public void PostgreSqlUpgrade_GeneratesGuardBeforeDestructiveOperations_AndRefusesAutomaticDowngrade()
    {
        using var context = new DbContext(new DbContextOptionsBuilder()
            .UseNpgsql("Host=unused;Database=unused;Username=unused").Options);
        var migration = new GenericOutbox();
        var commands = context.GetService<IMigrationsSqlGenerator>().Generate(migration.UpOperations);
        var sql = string.Join("\n", commands.Select(command => command.CommandText));
        var guard = sql.IndexOf("RAISE EXCEPTION", StringComparison.Ordinal);
        var destructive = sql.IndexOf("DROP COLUMN", StringComparison.Ordinal);
        Assert.True(guard >= 0 && destructive > guard);
        Assert.Contains("LOCK TABLE", sql);
        Assert.Contains("\"Status\" <> 1", sql);
        Assert.Contains("legacy.completed", sql);
        Assert.Contains("SchemaVersion", sql);
        Assert.Contains("NextAttemptAtUtc", sql);
        Assert.Throws<NotSupportedException>(() => migration.DownOperations);
    }
}
