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
    [Fact]
    public void OrderingRemoval_GuardsBothDirections_AndPreservesReceiptsPayloadsAndHeaders()
    {
        using var context = new DbContext(new DbContextOptionsBuilder()
            .UseNpgsql("Host=unused;Database=unused;Username=unused").Options);
        var migration = new RemoveMessageOrdering();
        var commands = context.GetService<IMigrationsSqlGenerator>().Generate(migration.UpOperations);
        var sql = string.Join("\n", commands.Select(command => command.CommandText));
        var guard = sql.IndexOf("RAISE EXCEPTION", StringComparison.Ordinal);
        Assert.True(guard >= 0 && sql.IndexOf("DROP TABLE", StringComparison.Ordinal) > guard);
        Assert.Contains("LOCK TABLE", sql);
        Assert.Contains("SELECT 1 FROM \"NOFInboxMessage\"", sql);
        Assert.Contains("SELECT 1 FROM \"NOFOutboxMessage\"", sql);
        Assert.Contains("BlockedSequence", sql);
        Assert.Equal(2, migration.UpOperations.OfType<Microsoft.EntityFrameworkCore.Migrations.Operations.DropTableOperation>().Count());
        Assert.Equal(6, migration.UpOperations.OfType<Microsoft.EntityFrameworkCore.Migrations.Operations.DropColumnOperation>().Count());
        Assert.All(migration.UpOperations.OfType<Microsoft.EntityFrameworkCore.Migrations.Operations.DropColumnOperation>(),
            operation => Assert.Contains(operation.Name, new[] { "OrderKey", "Sequence", "CompletesOrderKey" }));
        Assert.Throws<NotSupportedException>(() => migration.DownOperations);
    }

}
