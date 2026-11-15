using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NHibernate.Dialect;
using NHibernate.Driver;
using NOF.Application;
using NOF.Hosting;
using NOF.Infrastructure.EntityFrameworkCore;
using NOF.Infrastructure.NHibernate;
using System.Data;
using Xunit;

namespace NOF.Infrastructure.Tests.Persistence;

public sealed class NHibernateNativeStoreTests
{
    [Fact]
    public async Task NativeStores_ClaimWithCompareAndSet_AndFenceExpiredOwners()
    {
        var file = Path.Combine(Path.GetTempPath(), $"nof-native-nh-{Guid.NewGuid():N}.db");
        try
        {
            var builder = Host.CreateApplicationBuilder();
            builder.AddNOF();
            builder.Services.AddOutbox();
            builder.UseNHibernate().WithConnectionString($"Data Source={file};Pooling=False")
                .WithOptions((configuration, _) =>
                {
                    configuration.DataBaseIntegration(database =>
                    { database.Driver<MicrosoftDataSqliteTestDriver>(); database.Dialect<SQLiteDialect>(); });
                    configuration.SetProperty("hbm2ddl.auto", "create-drop");
                    configuration.SetProperty("hbm2ddl.keywords", "none");
                });
            using var host = builder.Build();
            using var scope = host.Services.CreateScope();
            var outbox = scope.ServiceProvider.GetRequiredService<IOutboxStore>();
            var id = Guid.NewGuid();
            outbox.Stage(new NOFOutboxMessage { Id = id, MessageType = "native", Payload = [1], Headers = "{}" });
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
            var first = Assert.Single(await outbox.AtomicClaimPendingMessagesAsync(claimTimeout: TimeSpan.FromSeconds(-1)).ToListAsync());
            var second = Assert.Single(await outbox.AtomicClaimPendingMessagesAsync().ToListAsync());
            Assert.Equal(id, first.Id);
            Assert.NotEqual(first.ClaimedBy, second.ClaimedBy);
            Assert.Equal(0, await outbox.AtomicMarkAsSentAsync([first]));
            await outbox.AtomicRecordDeliveryFailureAsync(first, "stale", true);
            Assert.Equal(1, await outbox.AtomicMarkAsSentAsync([second]));

        }
        finally
        {
            if (File.Exists(file))
            { File.Delete(file); }
        }
    }

}

public sealed class MicrosoftDataSqliteTestDriver : ReflectionBasedDriver
{
    public MicrosoftDataSqliteTestDriver() : base("Microsoft.Data.Sqlite", "Microsoft.Data.Sqlite.SqliteConnection", "Microsoft.Data.Sqlite.SqliteCommand") { }
    public override bool UseNamedPrefixInSql => true;
    public override bool UseNamedPrefixInParameter => true;
    public override string NamedPrefix => "@";
    public override bool SupportsMultipleOpenReaders => false;
}
