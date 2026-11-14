using Microsoft.Extensions.DependencyInjection;
using NOF.Abstraction;
using NOF.Application;
using Xunit;

namespace NOF.Infrastructure.Tests.Persistence;

public sealed class InMemoryServiceCollectionExtensionsTests
{
    private static ServiceProvider CreateProvider()
    {
        var services = new ServiceCollection();
        services.AddInMemoryMessagingPersistence();
        services.AddInbox();
        services.AddOutbox(_ => { });
        return services.BuildServiceProvider();
    }
    private static NOFOutboxMessage Intent() => new() { Id = Guid.NewGuid(), MessageType = "test", Payload = [1], Headers = "{}" };

    [Fact]
    public async Task Staging_IsInvisibleUntilSave_AndClaimsAreFenced()
    {
        using var provider = CreateProvider();
        using var scope = provider.CreateScope();
        var outbox = scope.ServiceProvider.GetRequiredService<IOutboxStore>();
        var row = Intent();
        outbox.Stage(row);
        using var observer = provider.CreateScope();
        var reader = observer.ServiceProvider.GetRequiredService<IOutboxStore>();
        Assert.Empty(await reader.AtomicClaimPendingMessagesAsync().ToListAsync());
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
        var first = Assert.Single(await reader.AtomicClaimPendingMessagesAsync(claimTimeout: TimeSpan.FromTicks(-1)).ToListAsync());
        var second = Assert.Single(await reader.AtomicClaimPendingMessagesAsync().ToListAsync());
        Assert.NotEqual(first.ClaimedBy, second.ClaimedBy);
        Assert.Equal(0, await reader.AtomicMarkAsSentAsync([first]));
        await reader.AtomicRecordDeliveryFailureAsync(first, "stale", true);
        Assert.Equal(1, await reader.AtomicMarkAsSentAsync([second]));
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExplicitTransaction_DoesNotPublishBeforeCommit(bool commit)
    {
        using var provider = CreateProvider();
        using var scope = provider.CreateScope();
        await using var transaction = await scope.ServiceProvider.GetRequiredService<ITransactionManager>().BeginTransactionAsync();
        scope.ServiceProvider.GetRequiredService<IOutboxStore>().Stage(Intent());
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
        using var observer = provider.CreateScope();
        var reader = observer.ServiceProvider.GetRequiredService<IOutboxStore>();
        Assert.Empty(await reader.AtomicClaimPendingMessagesAsync().ToListAsync());
        if (commit)
        { await transaction.CommitAsync(); }
        else
        { await transaction.RollbackAsync(); }
        Assert.Equal(commit ? 1 : 0, (await reader.AtomicClaimPendingMessagesAsync().ToListAsync()).Count);
    }
    [Fact]
    public async Task ReceiptCompletion_AndStagedOutbox_RollBackTogether()
    {
        using var provider = CreateProvider();
        using var scope = provider.CreateScope();
        var inbox = scope.ServiceProvider.GetRequiredService<IInboxStore>();
        var message = NOFInboxMessage.Create(Guid.NewGuid(), "route", [1], "{}");
        await inbox.ReceiveAsync(message, default);
        var claimed = Assert.Single(await inbox.AtomicClaimPendingMessagesAsync(10, TimeSpan.FromMinutes(1)).ToListAsync());
        await using (var transaction = await scope.ServiceProvider.GetRequiredService<ITransactionManager>().BeginTransactionAsync())
        {
            var pending = await inbox.FindPendingAsync(claimed, default);
            pending!.Status = InboxMessageStatus.Processed;
            scope.ServiceProvider.GetRequiredService<IOutboxStore>().Stage(Intent());
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
            await transaction.RollbackAsync();
        }
        using var observer = provider.CreateScope();
        Assert.NotNull(await observer.ServiceProvider.GetRequiredService<IInboxStore>().FindPendingAsync(claimed, default));
        Assert.Empty(await observer.ServiceProvider.GetRequiredService<IOutboxStore>().AtomicClaimPendingMessagesAsync().ToListAsync());
    }
    [Fact]
    public async Task TenantState_IsIsolated()
    {
        using var provider = CreateProvider();
        using (Context.PushCurrent(Context.Empty.WithTenantId("first")))
        using (var scope = provider.CreateScope())
        { scope.ServiceProvider.GetRequiredService<IOutboxStore>().Stage(Intent()); await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync(); }
        using (Context.PushCurrent(Context.Empty.WithTenantId("second")))
        using (var scope = provider.CreateScope())
        { Assert.Empty(await scope.ServiceProvider.GetRequiredService<IOutboxStore>().AtomicClaimPendingMessagesAsync().ToListAsync()); }
        using (Context.PushCurrent(Context.Empty.WithTenantId("first")))
        using (var scope = provider.CreateScope())
        { Assert.Single(await scope.ServiceProvider.GetRequiredService<IOutboxStore>().AtomicClaimPendingMessagesAsync().ToListAsync()); }
    }
    [Fact]
    public async Task SavingCompletedReceiptAgain_DoesNotRepeatItsStateTransition()
    {
        using var provider = CreateProvider();
        using var scope = provider.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IInboxStore>();
        await store.ReceiveAsync(NOFInboxMessage.Create(Guid.NewGuid(), "route", [1], "{}"), default);
        var claimed = Assert.Single(await store.AtomicClaimPendingMessagesAsync(1, TimeSpan.FromMinutes(1)).ToListAsync());
        var row = await store.FindPendingAsync(claimed, default);
        row!.Status = InboxMessageStatus.Processed;
        row.ProcessedAtUtc = DateTime.UtcNow;
        row.ClaimedBy = null;
        row.ClaimExpiresAtUtc = null;
        var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWork>();
        await unitOfWork.SaveChangesAsync();
        await unitOfWork.SaveChangesAsync();
        Assert.Equal(1, await store.CleanupAsync(DateTime.UtcNow.AddSeconds(1), default));
    }

}
