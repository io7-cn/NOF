using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NOF.Abstraction;
using NOF.Application;
using NOF.Hosting;
using System.Collections.Concurrent;
using Xunit;

namespace NOF.Infrastructure.Tests;

public sealed class GenericOutboxTests
{
    [Fact]
    public async Task CustomStore_CanStageWithoutAnOrmOrTransport_WhenDependencyValidationIsEnabled()
    {
        var store = new Moq.Mock<IOutboxStore>();
        var services = new ServiceCollection();
        services.AddSingleton(store.Object);
        services.AddSingleton<IObjectSerializer, JsonObjectSerializer>();
        services.AddSingleton<Probe>();
        services.AddOutbox(outbox => outbox.Add<Work, Handler>("custom.work"));
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        using var scope = provider.CreateScope();
        var id = await scope.ServiceProvider.GetRequiredService<IOutbox>().EnqueueAsync(new Work(), Context.Empty);
        store.Verify(value => value.Stage(Moq.It.Is<NOFOutboxMessage>(record => record.Id == id && record.MessageType == "custom.work")), Moq.Times.Once);
        Assert.Null(scope.ServiceProvider.GetService<IPersistenceSession>());
    }

    [Fact]
    public async Task Enqueue_SnapshotsPayloadAndAllowedMetadata_WithoutSavingOrConstructingHandler()
    {
        using var host = CreateHost();
        using var scope = host.Services.CreateScope();
        var value = new Work { Value = "before" };
        var id = await scope.ServiceProvider.GetRequiredService<IOutbox>().EnqueueAsync(value,
            Context.Empty.WithItem("Authorization", "secret").WithItem("arbitrary", "private"));
        value.Value = "after";
        using (var observer = host.Services.CreateScope())
        {
            Assert.False(await observer.ServiceProvider.GetRequiredService<IPersistenceSession>().Set<NOFOutboxMessage>().AnyAsync());
        }
        Assert.Empty(host.Services.GetRequiredService<Probe>().Attempts);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
        var record = await ReadAsync(host, id);
        Assert.Equal("work", record.MessageType);
        Assert.Equal(2, record.SchemaVersion);
        Assert.Equal("before", host.Services.GetRequiredService<IObjectSerializer>().Deserialize<Work>(record.Payload)!.Value);
        Assert.DoesNotContain("secret", record.Headers);
        Assert.DoesNotContain("private", record.Headers);
        Assert.Equal(id, record.Id);
    }

    [Fact]
    public async Task GenericHandler_RetriesWithSameId_AndUsesAFreshScopePerAttempt()
    {
        using var host = CreateHost();
        var id = await StageAsync(host, new Work { Value = "retry" });
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await host.StartAsync(timeout.Token);
        try
        {
            await WaitForAsync(host, id, OutboxMessageStatus.Sent, timeout.Token);
            var attempts = host.Services.GetRequiredService<Probe>().Attempts.ToArray();
            Assert.Equal([1, 2], attempts.Select(attempt => attempt.Number));
            Assert.All(attempts, attempt => Assert.Equal(id, attempt.Id));
            Assert.Equal(2, attempts.Select(attempt => attempt.Scope).Distinct().Count());
            Assert.All(attempts, attempt => Assert.Equal(attempt.Tenant, attempt.AmbientTenant));
        }
        finally
        {
            await host.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task UnknownSchema_IsPermanent_BlocksItsStream_AndDoesNotBlockIndependentWork()
    {
        using var host = CreateHost();
        Guid unknown;
        Guid follower;
        using (var scope = host.Services.CreateScope())
        {
            var outbox = scope.ServiceProvider.GetRequiredService<IOutbox>();
            var options = new OutboxEnqueueOptions { OrderKey = "stream" };
            unknown = await outbox.EnqueueAsync(new Work { Value = "unknown" }, Context.Empty, options);
            follower = await outbox.EnqueueAsync(new Work { Value = "follower" }, Context.Empty, options);
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
        }
        using (var scope = host.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<IPersistenceSession>().Set<NOFOutboxMessage>()
                .Where(record => record.Id == unknown).ExecuteUpdateAsync(setters => setters.SetProperty(record => record.SchemaVersion, 999));
        }
        var independent = await StageAsync(host, new Work { Value = "independent" });
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await host.StartAsync(timeout.Token);
        try
        {
            var failed = await WaitForAsync(host, unknown, OutboxMessageStatus.Failed, timeout.Token);
            Assert.Equal(1, failed.RetryCount);
            await WaitForAsync(host, independent, OutboxMessageStatus.Sent, timeout.Token);
            var blocked = await ReadAsync(host, follower);
            Assert.Equal(OutboxMessageStatus.Pending, blocked.Status);
            Assert.Equal(0, blocked.RetryCount);
            Assert.Equal("independent", Assert.Single(host.Services.GetRequiredService<Probe>().Attempts).Value);
        }
        finally
        {
            await host.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task PermanentAdapterFailure_DoesNotConsumeRetryBudget()
    {
        using var host = CreateHost();
        var id = await StageAsync(host, new Work { Value = "permanent" });
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await host.StartAsync(timeout.Token);
        try
        {
            var failed = await WaitForAsync(host, id, OutboxMessageStatus.Failed, timeout.Token);
            Assert.Equal(1, failed.RetryCount);
            Assert.Null(failed.NextAttemptAtUtc);
            Assert.Single(host.Services.GetRequiredService<Probe>().Attempts);
        }
        finally
        {
            await host.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task ExpiredClaimOwner_CannotCompleteReleaseOrFailNewOwnersRecord()
    {
        using var host = CreateHost();
        var id = await StageAsync(host, new Work { Value = "fenced" });
        using var scope = host.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IOutboxStore>();
        var unclaimed = await ReadAsync(host, id);
        Assert.Equal(0, await store.AtomicMarkAsSentAsync([unclaimed]));
        await store.AtomicReleaseClaimsAsync([unclaimed]);
        await store.AtomicRecordDeliveryFailureAsync(unclaimed, "not an owner", permanent: true);
        var first = Assert.Single(await store.AtomicClaimPendingMessagesAsync().ToListAsync());
        await scope.ServiceProvider.GetRequiredService<IPersistenceSession>().Set<NOFOutboxMessage>()
            .Where(record => record.Id == id).ExecuteUpdateAsync(setters => setters.SetProperty(record => record.ClaimExpiresAtUtc, DateTime.UtcNow.AddSeconds(-1)));
        var second = Assert.Single(await store.AtomicClaimPendingMessagesAsync().ToListAsync());
        Assert.NotEqual(first.ClaimedBy, second.ClaimedBy);
        Assert.Equal(0, await store.AtomicMarkAsSentAsync([first]));
        await store.AtomicReleaseClaimsAsync([first]);
        await store.AtomicRecordDeliveryFailureAsync(first, "stale", permanent: true);
        var record = await ReadAsync(host, id);
        Assert.Equal(second.ClaimedBy, record.ClaimedBy);
        Assert.Equal(2, record.RetryCount);
        Assert.Equal(OutboxMessageStatus.Pending, record.Status);
        Assert.Equal(1, await store.AtomicMarkAsSentAsync([second]));
    }

    [Fact]
    public async Task RetryDelay_PreventsEarlyReclaim()
    {
        using var host = CreateHost(TimeSpan.FromHours(1));
        await StageAsync(host, new Work { Value = "delayed" });
        using var scope = host.Services.CreateScope();
        var store = scope.ServiceProvider.GetRequiredService<IOutboxStore>();
        var first = Assert.Single(await store.AtomicClaimPendingMessagesAsync().ToListAsync());
        await store.AtomicRecordDeliveryFailureAsync(first, "retry");
        Assert.Empty(await store.AtomicClaimPendingMessagesAsync().ToListAsync());
    }

    [Fact]
    public async Task Shutdown_LeavesUncertainAttemptLeased_AndReleasesUnstartedWork()
    {
        using var host = CreateHost();
        var first = await StageAsync(host, new Work { Value = "wait" });
        var second = await StageAsync(host, new Work { Value = "unstarted" });
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await host.StartAsync(timeout.Token);
        try
        {
            await host.Services.GetRequiredService<Probe>().Started.Task.WaitAsync(timeout.Token);
        }
        finally
        {
            await host.StopAsync(CancellationToken.None);
        }
        var uncertain = await ReadAsync(host, first);
        Assert.Equal(OutboxMessageStatus.Pending, uncertain.Status);
        Assert.Equal(1, uncertain.RetryCount);
        Assert.NotNull(uncertain.ClaimedBy);
        var unstarted = await ReadAsync(host, second);
        Assert.Equal(OutboxMessageStatus.Pending, unstarted.Status);
        Assert.Equal(0, unstarted.RetryCount);
        Assert.Null(unstarted.ClaimedBy);
    }

    [Fact]
    public async Task Enqueue_RejectsTenantMismatchAndUnregisteredTypes_BeforeStaging()
    {
        using var host = CreateHost();
        using var scope = host.Services.CreateScope();
        var outbox = scope.ServiceProvider.GetRequiredService<IOutbox>();
        await Assert.ThrowsAsync<InvalidOperationException>(() => outbox.EnqueueAsync(new Work(), Context.Empty.WithTenantId("other")).AsTask());
        await Assert.ThrowsAsync<InvalidOperationException>(() => outbox.EnqueueAsync("unregistered", Context.Empty).AsTask());
        await Assert.ThrowsAsync<ArgumentException>(() => outbox.EnqueueAsync(new Work(), Context.Empty,
            new OutboxEnqueueOptions { CompletesOrderKey = true }).AsTask());
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
        Assert.False(await scope.ServiceProvider.GetRequiredService<IPersistenceSession>().Set<NOFOutboxMessage>().AnyAsync());
    }

    [Fact]
    public void Registrations_AreHostLocalUnambiguousAndFrozenOnUse()
    {
        var first = new ServiceCollection().AddOutbox(outbox => outbox.Add<Work, Handler>("work", 2));
        Assert.Throws<InvalidOperationException>(() => first.AddOutbox(outbox => outbox.Add<Work, Handler>("other", 1)));
        using var one = first.BuildServiceProvider();
        using var two = new ServiceCollection().AddOutbox(outbox => outbox.Add<Work, Handler>("other", 1)).BuildServiceProvider();
        Assert.Equal("work", one.GetRequiredService<OutboxRegistry>().Get(typeof(Work)).MessageType);
        Assert.Equal("other", two.GetRequiredService<OutboxRegistry>().Get(typeof(Work)).MessageType);
        Assert.Throws<InvalidOperationException>(() => first.AddOutbox(outbox => outbox.Add<string, StringHandler>("late")));
        Assert.Null(typeof(IOutbox).Assembly.GetType("NOF.Application.IMessageDispatcher"));
    }

    private static IHost CreateHost(TimeSpan? retryDelay = null)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.AddNOF();
        builder.Services.AddSingleton<Probe>();
        builder.Services.AddOutbox(outbox => outbox.Add<Work, Handler>("work", 2));
        builder.Services.Configure<TransactionalMessageOptions>(options =>
        {
            options.Outbox.PollingInterval = TimeSpan.FromMilliseconds(10);
            options.Outbox.RetryDelay = retryDelay ?? TimeSpan.FromMilliseconds(100);
        });
        return builder.Build();
    }

    private static async Task<Guid> StageAsync(IHost host, Work value)
    {
        using var scope = host.Services.CreateScope();
        var id = await scope.ServiceProvider.GetRequiredService<IOutbox>().EnqueueAsync(value, Context.Empty);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
        return id;
    }

    private static async Task<NOFOutboxMessage> ReadAsync(IHost host, Guid id)
    {
        using var scope = host.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IPersistenceSession>().Set<NOFOutboxMessage>()
            .AsNoTracking().SingleAsync(record => record.Id == id);
    }

    private static async Task<NOFOutboxMessage> WaitForAsync(IHost host, Guid id, OutboxMessageStatus status, CancellationToken ct)
    {
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var record = await ReadAsync(host, id);
            if (record.Status == status)
            {
                return record;
            }
            await Task.Delay(10, ct);
        }
    }

    public sealed class Work { public string Value { get; set; } = ""; }
    public sealed record Attempt(Guid Id, int Number, Guid Scope, string Value, string Tenant, string AmbientTenant);
    public sealed class Probe
    {
        public ConcurrentQueue<Attempt> Attempts { get; } = new();
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
    public sealed class Handler(Probe probe) : IOutboxHandler<Work>
    {
        private readonly Guid _scope = Guid.NewGuid();
        public async ValueTask HandleAsync(Work message, OutboxDeliveryContext context, CancellationToken cancellationToken)
        {
            probe.Attempts.Enqueue(new(context.MessageId, context.Attempt, _scope, message.Value, context.Context.TenantId, Context.Current.TenantId));
            if (message.Value == "retry" && context.Attempt == 1)
            {
                throw new InvalidOperationException("Try again");
            }
            if (message.Value == "permanent")
            {
                throw new OutboxPermanentFailureException("Business delivery rejected");
            }
            if (message.Value == "wait")
            {
                probe.Started.TrySetResult();
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }
        }
    }
    public sealed class StringHandler : IOutboxHandler<string>
    {
        public ValueTask HandleAsync(string message, OutboxDeliveryContext context, CancellationToken cancellationToken) => ValueTask.CompletedTask;
    }
}
