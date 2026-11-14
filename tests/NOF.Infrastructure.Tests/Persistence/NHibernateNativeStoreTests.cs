using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NHibernate.Dialect;
using NHibernate.Driver;
using NOF.Application;
using NOF.Hosting;
using NOF.Hosting.AspNetCore.Extension.OidcServer;
using NOF.Infrastructure.EntityFrameworkCore;
using NOF.Infrastructure.NHibernate;
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
            builder.Services.AddOutbox(_ => { });
            builder.Services.AddInbox();
            builder.Services.AddOidcNHibernate();
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
            var inbox = scope.ServiceProvider.GetRequiredService<IInboxStore>();
            await inbox.ReceiveAsync(NOFInboxMessage.Create(id, "one", [1], "{}"), default);
            await inbox.ReceiveAsync(NOFInboxMessage.Create(id, "two", [1], "{}"), default);
            var claimed = await inbox.AtomicClaimPendingMessagesAsync(10, TimeSpan.FromMinutes(1)).ToListAsync();
            Assert.Equal(2, claimed.Count);
            Assert.Equal(2, claimed.Select(row => row.Route).Distinct().Count());
            Assert.All(claimed, row => Assert.Equal(1, row.RetryCount));
            var clients = scope.ServiceProvider.GetRequiredService<IOAuthClientStore>();
            clients.Add(new OAuthClient { ClientId = "native-client", DisplayName = "Native", CreatedAtUtc = DateTime.UtcNow, UpdatedAtUtc = DateTime.UtcNow });
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
            using var verification = host.Services.CreateScope();
            Assert.Equal("Native", (await verification.ServiceProvider.GetRequiredService<IOAuthClientStore>().FindAsync("native-client", default))!.DisplayName);

        }
        finally
        {
            if (File.Exists(file))
            { File.Delete(file); }
        }
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NativeReceiptSave_RejectsAWorkerWhoseClaimWasReplaced(bool useNHibernate)
    {
        var file = Path.Combine(Path.GetTempPath(), $"nof-native-fence-{Guid.NewGuid():N}.db");
        try
        {
            var builder = Host.CreateApplicationBuilder();
            builder.AddNOF();
            builder.Services.AddInbox();
            if (useNHibernate)
            {
                builder.UseNHibernate().WithConnectionString($"Data Source={file};Pooling=False")
                    .WithOptions((configuration, _) =>
                    {
                        configuration.DataBaseIntegration(database => { database.Driver<MicrosoftDataSqliteTestDriver>(); database.Dialect<SQLiteDialect>(); });
                        configuration.SetProperty("hbm2ddl.auto", "create-drop");
                        configuration.SetProperty("hbm2ddl.keywords", "none");
                    });
            }
            else
            {
                builder.UseDbContext<NOFDbContext>().WithConnectionString($"Data Source={file};Pooling=False")
                    .WithOptions((options, connection) => options.UseSqlite(connection));
            }
            using var host = builder.Build();
            using var firstScope = host.Services.CreateScope();
            var firstStore = firstScope.ServiceProvider.GetRequiredService<IInboxStore>();
            await firstStore.ReceiveAsync(NOFInboxMessage.Create(Guid.NewGuid(), "route", [1], "{}"), default);
            var first = Assert.Single(await firstStore.AtomicClaimPendingMessagesAsync(1, TimeSpan.FromSeconds(-1)).ToListAsync());
            var pending = await firstStore.FindPendingAsync(first, default);
            using var secondScope = host.Services.CreateScope();
            var second = Assert.Single(await secondScope.ServiceProvider.GetRequiredService<IInboxStore>().AtomicClaimPendingMessagesAsync(1, TimeSpan.FromMinutes(1)).ToListAsync());
            Assert.NotEqual(first.ClaimedBy, second.ClaimedBy);
            pending!.Status = InboxMessageStatus.Processed;
            pending.ClaimedBy = null;
            pending.ClaimExpiresAtUtc = null;
            await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => firstScope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync());
            using var verification = host.Services.CreateScope();
            Assert.NotNull(await verification.ServiceProvider.GetRequiredService<IInboxStore>().FindPendingAsync(second, default));
        }
        finally { if (File.Exists(file)) { File.Delete(file); } }
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
