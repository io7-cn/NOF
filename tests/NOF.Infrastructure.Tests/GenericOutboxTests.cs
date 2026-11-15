using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NOF.Abstraction;
using NOF.Application;
using NOF.Hosting;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json.Nodes;
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
        services.AddOptions();
        services.AddTransactionalMessages().AddOutboxHandler<Work, Handler>();
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        using var scope = provider.CreateScope();
        var id = await scope.ServiceProvider.GetRequiredService<IOutbox>().EnqueueAsync(new Work(), Context.Empty);
        store.Verify(value => value.Stage(Moq.It.Is<NOFOutboxMessage>(record => record.Id == id && record.MessageType == typeof(Work).ToString())), Moq.Times.Once);
        Assert.Null(scope.ServiceProvider.GetService<DbContext>());
    }

    [Fact]
    public async Task Enqueue_SnapshotsPayloadAndCompleteJsonContext_WithoutSavingOrConstructingHandler()
    {
        using var host = CreateHost();
        using var scope = host.Services.CreateScope();
        var value = new Work { Value = "before" };
        var headers = new Dictionary<string, string?> { ["partition"] = "before", ["nullable"] = null };
        var id = await scope.ServiceProvider.GetRequiredService<IOutbox>().EnqueueAsync(value,
            Context.Empty.WithItem("correlation", "original").WithItem("count", 7).WithItem("enabled", true).WithItem("empty", null), headers);
        headers["partition"] = "after";
        value.Value = "after";
        using (var observer = host.Services.CreateScope())
        {
            Assert.False(await observer.ServiceProvider.GetRequiredService<DbContext>().Set<NOFOutboxMessage>().AnyAsync());
        }
        Assert.Empty(host.Services.GetRequiredService<Probe>().Attempts);
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
        var record = await ReadAsync(host, id);
        Assert.Equal(typeof(Work).ToString(), record.MessageType);
        Assert.Equal("before", host.Services.GetRequiredService<IObjectSerializer>().Deserialize<Work>(record.Payload)!.Value);
        Assert.Equal(id, record.Id);
        var metadata = host.Services.GetRequiredService<IObjectSerializer>().Deserialize<Dictionary<string, string?>>(record.Headers)!;
        Assert.Equal("before", metadata["partition"]);
        Assert.Null(metadata["nullable"]);
        Assert.Equal(id.ToString(), metadata[NOFAbstractionConstants.Transport.Headers.MessageId]);
        var snapshot = JsonNode.Parse(metadata[NOFAbstractionConstants.Transport.Headers.Context]!)!.AsObject();
        Assert.Equal("original", snapshot["correlation"]!.GetValue<string>());
        Assert.Equal(7, snapshot["count"]!.GetValue<int>());
        Assert.True(snapshot["enabled"]!.GetValue<bool>());
        Assert.True(snapshot.ContainsKey("empty"));
        Assert.Null(snapshot["empty"]);
    }

    [Fact]
    public async Task GenericHandler_RetriesWithSameId_AndUsesAFreshScopePerAttempt()
    {
        using var host = CreateHost();
        Guid id;
        using (var scope = host.Services.CreateScope())
        {
            using var producer = new Activity("enqueue").Start();
            var headers = new Dictionary<string, string?> { ["partition"] = "unchanged", ["nullable"] = null };
            id = await scope.ServiceProvider.GetRequiredService<IOutbox>().EnqueueAsync(new Work { Value = "retry" },
                Context.Empty.WithItem("correlation", "unchanged").WithItem("count", 7), headers);
            headers["partition"] = "mutated";
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
        }
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await host.StartAsync(timeout.Token);
        try
        {
            var sent = await WaitForAsync(host, id, OutboxMessageStatus.Sent, timeout.Token);
            Assert.NotNull(sent.TraceParent);
            var attempts = host.Services.GetRequiredService<Probe>().Attempts.ToArray();
            Assert.Equal([1, 2], attempts.Select(attempt => attempt.Number));
            Assert.All(attempts, attempt => Assert.Equal(id, attempt.Id));
            Assert.Equal(2, attempts.Select(attempt => attempt.Scope).Distinct().Count());
            Assert.All(attempts, attempt => Assert.Equal(attempt.Tenant, attempt.AmbientTenant));
            var envelopes = host.Services.GetRequiredService<Probe>().Envelopes.ToArray();
            Assert.Equal(2, envelopes.Length);
            Assert.All(envelopes, headers =>
            {
                Assert.Equal("unchanged", headers["partition"]);
                Assert.Null(headers["nullable"]);
                Assert.Equal(id.ToString(), headers[NOFAbstractionConstants.Transport.Headers.MessageId]);
                Assert.Equal("host", headers[NOFAbstractionConstants.Transport.Headers.TenantId]);
                Assert.Equal(sent.TraceParent, headers[NOFAbstractionConstants.Transport.Headers.TraceParent]);
                var snapshot = JsonNode.Parse(headers[NOFAbstractionConstants.Transport.Headers.Context]!)!.AsObject();
                Assert.Equal("unchanged", snapshot["correlation"]!.GetValue<string>());
                Assert.Equal(7, snapshot["count"]!.GetValue<int>());
            });
        }
        finally
        {
            await host.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task UnknownSchema_IsPermanent_AndDoesNotBlockOtherWork()
    {
        using var host = CreateHost();
        Guid unknown;
        Guid follower;
        using (var scope = host.Services.CreateScope())
        {
            var outbox = scope.ServiceProvider.GetRequiredService<IOutbox>();
            var headers = new Dictionary<string, string?> { ["partition"] = "stream" };
            unknown = await outbox.EnqueueAsync(new Work { Value = "unknown" }, Context.Empty, headers);
            follower = await outbox.EnqueueAsync(new Work { Value = "follower" }, Context.Empty, headers);
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
        }
        using (var scope = host.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<DbContext>().Set<NOFOutboxMessage>()
                .Where(record => record.Id == unknown).ExecuteUpdateAsync(setters => setters.SetProperty(record => record.MessageType, "unregistered.message"));
        }
        var independent = await StageAsync(host, new Work { Value = "independent" });
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await host.StartAsync(timeout.Token);
        try
        {
            var failed = await WaitForAsync(host, unknown, OutboxMessageStatus.Failed, timeout.Token);
            Assert.Equal(1, failed.RetryCount);
            await WaitForAsync(host, independent, OutboxMessageStatus.Sent, timeout.Token);
            await WaitForAsync(host, follower, OutboxMessageStatus.Sent, timeout.Token);
            Assert.Equal(new[] { "follower", "independent" }.Order(), host.Services.GetRequiredService<Probe>().Attempts.Select(attempt => attempt.Value).Order());
        }
        finally
        {
            await host.StopAsync(CancellationToken.None);
        }
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("{\"nested\":{\"key\":1}}")]
    [InlineData("{\"X-Tenant-Id\":\"another-tenant\"}")]
    [InlineData("invalid json")]
    public async Task InvalidPersistedContext_IsPermanentBeforeHandlerConstruction(string snapshot)
    {
        using var host = CreateHost();
        var id = await StageAsync(host, new Work { Value = "corrupt context" });
        var record = await ReadAsync(host, id);
        var serializer = host.Services.GetRequiredService<IObjectSerializer>();
        var headers = serializer.Deserialize<Dictionary<string, string?>>(record.Headers)!;
        headers[NOFAbstractionConstants.Transport.Headers.Context] = snapshot;
        using (var scope = host.Services.CreateScope())
        {
            var json = serializer.SerializeToText(headers, typeof(Dictionary<string, string?>));
            await scope.ServiceProvider.GetRequiredService<DbContext>().Set<NOFOutboxMessage>()
                .Where(message => message.Id == id).ExecuteUpdateAsync(update => update.SetProperty(message => message.Headers, json));
        }
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await host.StartAsync(timeout.Token);
        try
        {
            var failed = await WaitForAsync(host, id, OutboxMessageStatus.Failed, timeout.Token);
            Assert.Equal(1, failed.RetryCount);
            Assert.Empty(host.Services.GetRequiredService<Probe>().Attempts);
        }
        finally
        {
            await host.StopAsync();
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
        await scope.ServiceProvider.GetRequiredService<DbContext>().Set<NOFOutboxMessage>()
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
        using (var scope = host.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<DbContext>().Set<NOFOutboxMessage>()
                .Where(record => record.Id == first).ExecuteUpdateAsync(setters => setters.SetProperty(record => record.CreatedAtUtc, DateTime.UtcNow.AddMinutes(-1)));
        }
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
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
        Assert.False(await scope.ServiceProvider.GetRequiredService<DbContext>().Set<NOFOutboxMessage>().AnyAsync());
    }

    [Theory]
    [InlineData("x-message-id")]
    [InlineData("x-tenant-id")]
    [InlineData("TRACEPARENT")]
    [InlineData("x-nof-context")]
    public async Task Enqueue_RejectsManagedHeaderOverrides_BeforeStaging(string name)
    {
        using var host = CreateHost();
        using var scope = host.Services.CreateScope();
        var outbox = scope.ServiceProvider.GetRequiredService<IOutbox>();
        await Assert.ThrowsAsync<ArgumentException>(() => outbox.EnqueueAsync(new Work(), Context.Empty,
            new Dictionary<string, string?> { [name] = "override" }).AsTask());
        await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
        Assert.False(await scope.ServiceProvider.GetRequiredService<DbContext>().Set<NOFOutboxMessage>().AnyAsync());
    }

    [Fact]
    public void Registrations_AreHostLocalUnambiguousAndFrozenOnUse()
    {
        var first = new ServiceCollection().AddTransactionalMessages().AddOutboxHandler<Work, Handler>();
        Assert.Throws<InvalidOperationException>(() => first.AddTransactionalMessages().AddOutboxHandler<Work, Handler>());
        using var one = first.BuildServiceProvider();
        using var two = new ServiceCollection().AddTransactionalMessages().AddOutboxHandler<Work, Handler>().BuildServiceProvider();
        Assert.Equal(typeof(Work).ToString(), one.GetRequiredService<OutboxRegistry>().Get(typeof(Work)).MessageType);
        Assert.Equal(typeof(Work).ToString(), two.GetRequiredService<OutboxRegistry>().Get(typeof(Work)).MessageType);
        Assert.NotSame(one.GetRequiredService<OutboxRegistry>(), two.GetRequiredService<OutboxRegistry>());
        Assert.Throws<InvalidOperationException>(() => first.AddTransactionalMessages().AddOutboxHandler<string, StringHandler>());
        Assert.Null(typeof(IOutbox).Assembly.GetType("NOF.Application.IMessageDispatcher"));
    }

    [Fact]
    public void ClosedGenericMessages_HaveDistinctAssemblyVersionIndependentInferredIdentities()
    {
        var services = new ServiceCollection()
            .AddOutboxHandler<Envelope<string>, EnvelopeHandler<string>>()
            .AddOutboxHandler<Envelope<int>, EnvelopeHandler<int>>();
        using var provider = services.BuildServiceProvider();
        var registry = provider.GetRequiredService<OutboxRegistry>();
        var text = registry.Get(typeof(Envelope<string>));
        var number = registry.Get(typeof(Envelope<int>));
        Assert.Equal(typeof(Envelope<string>).ToString(), text.MessageType);
        Assert.NotEqual(text.MessageType, number.MessageType);
        Assert.DoesNotContain("Version=", text.MessageType);
        Assert.DoesNotContain("PublicKeyToken=", text.MessageType);
        Assert.Same(text, registry.Get(text.MessageType));
    }

    public sealed record Envelope<T>(T Value);
    public sealed class EnvelopeHandler<T> : OutboxHandler<Envelope<T>>
    {
        public override ValueTask HandleAsync(Envelope<T> message, OutboxDeliveryContext context, CancellationToken cancellationToken)
            => ValueTask.CompletedTask;
    }

    private static IHost CreateHost(TimeSpan? retryDelay = null)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.AddNOF();
        builder.AddNativeTestPersistence();
        builder.Services.AddSingleton<Probe>();
        builder.Services.AddTransactionalMessages().AddOutboxHandler<Work, Handler>();
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
        return await scope.ServiceProvider.GetRequiredService<DbContext>().Set<NOFOutboxMessage>()
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
        public ConcurrentQueue<Dictionary<string, string?>> Envelopes { get; } = new();
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
    public sealed class Handler(Probe probe) : OutboxHandler<Work>
    {
        private readonly Guid _scope = Guid.NewGuid();
        public override async ValueTask HandleAsync(Work message, OutboxDeliveryContext context, CancellationToken cancellationToken)
        {
            if (message.Value == "retry")
            {
                Assert.Equal("unchanged", context.Headers["PARTITION"]);
                Assert.Throws<NotSupportedException>(() => ((IDictionary<string, string?>)context.Headers)["partition"] = "changed");
                Assert.Equal("unchanged", context.Context["correlation"]!.GetValue<string>());
                Assert.Equal(7, Context.Current["count"]!.GetValue<int>());
            }
            probe.Attempts.Enqueue(new(context.MessageId, context.Attempt, _scope, message.Value, context.Context.TenantId, Context.Current.TenantId));
            probe.Envelopes.Enqueue(new(context.Headers));
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
    public sealed class StringHandler : OutboxHandler<string>
    {
        public override ValueTask HandleAsync(string message, OutboxDeliveryContext context, CancellationToken cancellationToken) => ValueTask.CompletedTask;
    }
}
