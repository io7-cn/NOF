using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NOF.Abstraction;
using NOF.Application;
using NOF.Contract;
using NOF.Hosting;
using NOF.Infrastructure;
using System.Collections.Concurrent;
using Xunit;

namespace NOF.Infrastructure.Tests.Notifications;

public sealed class NotificationDispatchIntegrationTests
{
    [Fact]
    public async Task FilterFailure_DoesNotBlockOtherReceiptsOrConsumeTechnicalRetries()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.AddNOF();
        builder.AddNativeTestPersistence();
        ConfigureMessages(builder.Services);
        builder.Services.Configure<TransactionalMessageOptions>(options => options.Inbox.PollingInterval = TimeSpan.FromMilliseconds(10));
        builder.Services.AddMemoryMessaging(bindings => bindings.MapSubscription("test.ordered.v1", "ordered", new ServiceOperationDescriptor<OrderedNotification, Empty>(typeof(INotificationOperations), "Ordered")));
        var calls = new List<int>();
        builder.Services.AddSingleton(calls);
        builder.Services.AddInvocationFilter<CountingDeny>();
        var logs = new CapturingLoggerProvider();
        builder.Logging.ClearProviders();
        builder.Logging.AddProvider(logs);
        using var host = builder.Build();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var rider = host.Services.GetRequiredService<MemoryMessageClient>();
        var serializer = host.Services.GetRequiredService<IObjectSerializer>();
        await PublishOrderedAsync(rider, serializer, new OrderedNotification(1), "Producer:42", 1, false, timeout.Token);
        await PublishOrderedAsync(rider, serializer, new OrderedNotification(2), "Producer:42", 2, true, timeout.Token);
        await host.StartAsync(timeout.Token);
        try
        {
            await WaitUntilAsync(() => logs.Messages.Contains("Inbox batch processed: 0 processed, 2 failed"), timeout.Token);
            using var scope = host.Services.CreateScope();
            var session = scope.ServiceProvider.GetRequiredService<DbContext>();
            var receipts = await session.Set<NOFInboxMessage>().AsNoTracking().OrderBy(item => item.CreatedAtUtc).ToListAsync(timeout.Token);
            Assert.Equal(InboxMessageStatus.Failed, receipts[0].Status);
            Assert.Equal(1, receipts[0].RetryCount);
            Assert.Equal(InboxMessageStatus.Failed, receipts[1].Status);
            Assert.Equal(1, receipts[1].RetryCount);
            Assert.Null(receipts[1].ClaimedBy);
            Assert.Equal([1, 2], calls);
        }
        finally
        {
            await host.StopAsync(CancellationToken.None);
        }
    }

    public sealed class CountingDeny(List<int> calls) : IInvocationFilter
    {
        public static IReadOnlyList<InvocationFailureCase> FailureCases => ServiceOperationPipelineTests.Deny.FailureCases;
        public ValueTask<object?> InvokeAsync(InvocationContext context, object input, InvocationDelegate next, CancellationToken ct)
        {
            calls.Add(((OrderedNotification)input).Value);
            return ValueTask.FromResult<object?>(new ServiceOperationPipelineTests.Denied("403"));
        }
    }

    [Fact]
    public async Task RejectedOperation_IsTerminalWithoutConstructingHandlerOrRetrying()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.AddNOF();
        builder.AddNativeTestPersistence();
        ConfigureMessages(builder.Services);
        builder.Services.AddInvocationFilter<ServiceOperationPipelineTests.Deny>();
        builder.Services.Configure<TransactionalMessageOptions>(options => options.Inbox.PollingInterval = TimeSpan.FromMilliseconds(10));
        builder.Services.AddMemoryMessaging(bindings => bindings.MapSubscription("test.failing.v1", "rejected", new ServiceOperationDescriptor<FailingNotification, Empty>(typeof(INotificationOperations), "Failing")));
        // The handler is deliberately unregistered: policy rejection must precede its construction.
        using var host = builder.Build();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await host.StartAsync(timeout.Token);
        try
        {
            using var scope = host.Services.CreateScope();
            await scope.ServiceProvider.GetRequiredService<MemoryMessageClient>().PublishAsync("test.failing.v1", new FailingNotification(), Context.Empty, cancellationToken: timeout.Token);
            var receipt = await WaitForInboxStatusAsync(host.Services, InboxMessageStatus.Failed, timeout.Token);
            Assert.Equal(1, receipt.RetryCount);
            Assert.Equal("memory:subscription:rejected", receipt.Route);
        }
        finally
        {
            await host.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task PublishAsync_ShouldDispatchConcreteNotificationToEveryConcreteHandler_EndToEnd()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.AddNOF();
        builder.AddNativeTestPersistence();
        ConfigureMessages(builder.Services);
        builder.Services.Configure<TransactionalMessageOptions>(static options =>
        {
            options.Inbox.PollingInterval = TimeSpan.FromMilliseconds(10);
            options.Inbox.BatchSize = 10;
            options.Outbox.PollingInterval = TimeSpan.FromMilliseconds(10);
        });

        builder.Services.AddMemoryMessaging(bindings => bindings.MapSubscription("test.concrete.v1", "first", new ServiceOperationDescriptor<ConcreteNotification, Empty>(typeof(INotificationOperations), "First")));
        builder.Services.AddMemoryMessaging(bindings => bindings.MapSubscription("test.concrete.v1", "second", new ServiceOperationDescriptor<ConcreteNotification, Empty>(typeof(INotificationOperations), "Second")));
        builder.Services.AddMemoryMessaging(bindings => bindings.MapSubscription("test.base.v1", "base", new ServiceOperationDescriptor<BaseNotification, Empty>(typeof(INotificationOperations), "Base")));

        builder.Services.AddSingleton<NotificationDispatchProbe>();
        builder.Services.AddScoped<FirstConcreteNotificationHandler>();
        builder.Services.AddScoped<SecondConcreteNotificationHandler>();
        builder.Services.AddScoped<BaseNotificationHandler>();

        using var host = builder.Build();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await host.StartAsync(timeout.Token);

        try
        {
            using (var scope = host.Services.CreateScope())
            {
                var publisher = scope.ServiceProvider.GetRequiredService<MemoryMessageClient>();
                await publisher.PublishAsync("test.concrete.v1", new ConcreteNotification("published"), Context.Empty, cancellationToken: timeout.Token);
            }

            var probe = host.Services.GetRequiredService<NotificationDispatchProbe>();
            await WaitUntilAsync(() => probe.FirstConcreteCount == 1 && probe.SecondConcreteCount == 1, timeout.Token);

            // Handler probes are signaled before the inbox transaction commits.
            var inboxMessages = await WaitForProcessedInboxMessagesAsync(host.Services, 2, timeout.Token);

            Assert.Equal(2, inboxMessages.Count);
            Assert.Single(inboxMessages.Select(static message => message.Id).Distinct());
            Assert.All(inboxMessages, static message => Assert.Equal(InboxMessageStatus.Processed, message.Status));
            Assert.Contains(inboxMessages, static message => message.Route == "memory:subscription:first");
            Assert.Contains(inboxMessages, static message => message.Route == "memory:subscription:second");
            Assert.DoesNotContain(inboxMessages, static message => message.Route == "memory:subscription:base");
            Assert.Equal(0, probe.BaseCount);
        }
        finally
        {
            await host.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task PublishAsync_WhenHandlerThrows_ShouldRetryAndMarkInboxAsFailed()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.AddNOF();
        builder.AddNativeTestPersistence();
        ConfigureMessages(builder.Services);
        builder.Services.Configure<TransactionalMessageOptions>(static options =>
        {
            options.Inbox.PollingInterval = TimeSpan.FromMilliseconds(10);
            options.Inbox.BatchSize = 10;
            options.Inbox.MaxRetryCount = 2;
        });

        builder.Services.AddMemoryMessaging(bindings => bindings.MapSubscription("test.failing.v1", "failing", new ServiceOperationDescriptor<FailingNotification, Empty>(typeof(INotificationOperations), "Failing")));
        builder.Services.AddSingleton<FailingNotificationProbe>();
        builder.Services.AddScoped<FailingNotificationHandler>();

        using var host = builder.Build();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await host.StartAsync(timeout.Token);

        try
        {
            using (var scope = host.Services.CreateScope())
            {
                var publisher = scope.ServiceProvider.GetRequiredService<MemoryMessageClient>();
                await publisher.PublishAsync("test.failing.v1", new FailingNotification(), Context.Empty, cancellationToken: timeout.Token);
            }

            var probe = host.Services.GetRequiredService<FailingNotificationProbe>();
            await WaitUntilAsync(() => probe.AttemptCount == 2, timeout.Token);
            var inboxMessage = await WaitForInboxStatusAsync(
                host.Services,
                InboxMessageStatus.Failed,
                timeout.Token);

            Assert.Equal(2, inboxMessage.RetryCount);
            Assert.Null(inboxMessage.ProcessedAtUtc);
            Assert.NotNull(inboxMessage.FailedAtUtc);
            Assert.Equal("Notification failed.", inboxMessage.ErrorMessage);
        }
        finally
        {
            await host.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task EnqueueAsync_WhenDeliveryFails_ShouldUseAllRetriesAndReportFailedBatch()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.AddNOF();
        builder.AddNativeTestPersistence();
        ConfigureMessages(builder.Services, failingDelivery: true);
        builder.Services.Configure<TransactionalMessageOptions>(static options =>
        {
            options.Outbox.PollingInterval = TimeSpan.FromMilliseconds(10);
            options.Outbox.BatchSize = 10;
            options.Outbox.MaxRetryCount = 2;
        });

        var rider = new AlwaysFailingOutboxHandler();
        builder.Services.AddSingleton(rider);
        var loggerProvider = new CapturingLoggerProvider();
        builder.Logging.ClearProviders();
        builder.Logging.AddProvider(loggerProvider);

        using var host = builder.Build();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await host.StartAsync(timeout.Token);

        try
        {
            using (var scope = host.Services.CreateScope())
            {
                var publisher = scope.ServiceProvider.GetRequiredService<IOutbox>();
                var dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();
                await publisher.EnqueueAsync(new FailingNotification(), Context.Empty, cancellationToken: timeout.Token);
                await dbContext.SaveChangesAsync(timeout.Token);
            }

            await WaitUntilAsync(() => rider.AttemptCount == 2, timeout.Token);
            var outboxMessage = await WaitForOutboxStatusAsync(
                host.Services,
                OutboxMessageStatus.Failed,
                timeout.Token);
            await WaitUntilAsync(
                () => loggerProvider.Messages.Contains("Outbox batch processed: 0 sent, 1 failed"),
                timeout.Token);

            Assert.Equal(2, outboxMessage.RetryCount);
            Assert.Null(outboxMessage.SentAtUtc);
            Assert.NotNull(outboxMessage.FailedAtUtc);
            Assert.Equal("Transport unavailable.", outboxMessage.ErrorMessage);
        }
        finally
        {
            await host.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task LegacyOrderingHeaders_AreOpaque_DoNotWaitForGapsOrCloseAStream()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.AddNOF();
        builder.AddNativeTestPersistence();
        ConfigureMessages(builder.Services);
        builder.Services.Configure<TransactionalMessageOptions>(static options =>
        {
            options.Inbox.PollingInterval = TimeSpan.FromMilliseconds(10);
            options.Inbox.BatchSize = 10;
        });

        builder.Services.AddMemoryMessaging(bindings => bindings.MapSubscription("test.ordered.v1", "ordered", new ServiceOperationDescriptor<OrderedNotification, Empty>(typeof(INotificationOperations), "Ordered")));
        builder.Services.AddSingleton<OrderedNotificationProbe>();
        builder.Services.AddScoped<OrderedNotificationHandler>();

        using var host = builder.Build();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await host.StartAsync(timeout.Token);

        try
        {
            var rider = host.Services.GetRequiredService<MemoryMessageClient>();
            var serializer = host.Services.GetRequiredService<IObjectSerializer>();
            const string orderKey = "ProducerService:Invoice:42";

            await PublishOrderedAsync(rider, serializer, new OrderedNotification(2), orderKey, 2, true, timeout.Token);
            await WaitUntilAsync(() => host.Services.GetRequiredService<OrderedNotificationProbe>().Values.Count == 1, timeout.Token);

            await PublishOrderedAsync(rider, serializer, new OrderedNotification(1), orderKey, 1, false, timeout.Token);
            var probe = host.Services.GetRequiredService<OrderedNotificationProbe>();
            await WaitUntilAsync(() => probe.Values.Count == 2, timeout.Token);

            Assert.Equal([2, 1], probe.Values);


            await PublishOrderedAsync(rider, serializer, new OrderedNotification(3), orderKey, 1, false, timeout.Token);
            await WaitUntilAsync(() => probe.Values.Count == 3, timeout.Token);
            Assert.Equal([2, 1, 3], probe.Values);
        }
        finally
        {
            await host.StopAsync(CancellationToken.None);
        }
    }

    private static Task PublishOrderedAsync(
        MemoryMessageClient rider,
        IObjectSerializer serializer,
        OrderedNotification notification,
        string orderKey,
        long sequence,
        bool completesOrderKey,
        CancellationToken cancellationToken)
    {
        var headers = new Dictionary<string, string?>
        {
            [NOFAbstractionConstants.Transport.Headers.MessageId] = Guid.NewGuid().ToString(),
            ["X-Order-Key"] = orderKey,
            ["X-Sequence"] = sequence.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["X-Completes-Order-Key"] = completesOrderKey.ToString()
        };
        return rider.PublishAsync("test.ordered.v1", notification, new OutboxDeliveryContext
        {
            MessageId = Guid.Parse(headers[NOFAbstractionConstants.Transport.Headers.MessageId]!),
            Context = Context.Empty,
            Attempt = 1,
            Headers = headers
        }, cancellationToken);
    }

    private static async Task WaitUntilAsync(Func<bool> condition, CancellationToken cancellationToken)
    {
        while (!condition())
        {
            cancellationToken.ThrowIfCancellationRequested();
            await Task.Delay(10, cancellationToken);
        }
    }

    private static async Task<List<NOFInboxMessage>> WaitForProcessedInboxMessagesAsync(
        IServiceProvider services,
        int expectedCount,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var scope = services.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();
            var messages = await dbContext.Set<NOFInboxMessage>()
                .AsNoTracking()
                .OrderBy(static message => message.Route)
                .ToListAsync(cancellationToken);
            if (messages.Count == expectedCount && messages.All(static message => message.Status == InboxMessageStatus.Processed))
            {
                return messages;
            }

            await Task.Delay(10, cancellationToken);
        }
    }

    private static async Task<NOFInboxMessage> WaitForInboxStatusAsync(
        IServiceProvider services,
        InboxMessageStatus status,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var scope = services.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();
            var message = await dbContext.Set<NOFInboxMessage>()
                .AsNoTracking()
                .SingleOrDefaultAsync(cancellationToken);
            if (message?.Status == status)
            {
                return message;
            }

            await Task.Delay(10, cancellationToken);
        }
    }

    private static async Task<NOFOutboxMessage> WaitForOutboxStatusAsync(
        IServiceProvider services,
        OutboxMessageStatus status,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var scope = services.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<DbContext>();
            var message = await dbContext.Set<NOFOutboxMessage>()
                .AsNoTracking()
                .SingleOrDefaultAsync(cancellationToken);
            if (message?.Status == status)
            {
                return message;
            }

            await Task.Delay(10, cancellationToken);
        }
    }

    private abstract record BaseNotification(string Value);

    private sealed record ConcreteNotification(string Value) : BaseNotification(Value);

    private sealed record OrderedNotification(int Value);

    private sealed record FailingNotification;

    private sealed class AlwaysFailingOutboxHandler : IOutboxHandler<FailingNotification>
    {
        private int _attemptCount;

        public int AttemptCount => Volatile.Read(ref _attemptCount);

        public ValueTask HandleAsync(FailingNotification message, OutboxDeliveryContext context,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _attemptCount);
            throw new InvalidOperationException("Transport unavailable.");
        }
    }

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        private readonly ConcurrentQueue<string> _messages = new();

        public IReadOnlyCollection<string> Messages => _messages;

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(_messages);

        public void Dispose()
        {
        }

        private sealed class CapturingLogger(ConcurrentQueue<string> messages) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull
                => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                messages.Enqueue(formatter(state, exception));
            }
        }
    }

    private sealed class NotificationDispatchProbe
    {
        private int _firstConcreteCount;
        private int _secondConcreteCount;
        private int _baseCount;

        public int FirstConcreteCount => Volatile.Read(ref _firstConcreteCount);

        public int SecondConcreteCount => Volatile.Read(ref _secondConcreteCount);

        public int BaseCount => Volatile.Read(ref _baseCount);

        public void MarkFirstConcrete() => Interlocked.Increment(ref _firstConcreteCount);

        public void MarkSecondConcrete() => Interlocked.Increment(ref _secondConcreteCount);

        public void MarkBase() => Interlocked.Increment(ref _baseCount);
    }

    private static void ConfigureMessages(IServiceCollection services, bool failingDelivery = false)
    {
        services.AddOutbox(outbox =>
        {
            outbox.Add<ConcreteNotification, NotificationOutboxHandler<ConcreteNotification>>("test.concrete.v1");
            outbox.Add<BaseNotification, NotificationOutboxHandler<BaseNotification>>("test.base.v1");
            outbox.Add<OrderedNotification, NotificationOutboxHandler<OrderedNotification>>("test.ordered.v1");
            if (failingDelivery)
            {
                outbox.Add<FailingNotification, AlwaysFailingOutboxHandler>("test.failing.v1");
            }
            else
            {
                outbox.Add<FailingNotification, NotificationOutboxHandler<FailingNotification>>("test.failing.v1");
            }
        });
        var registry = services.GetOrAddSingleton<ApplicationServiceRegistry>();
        registry.Add(new(typeof(INotificationOperations), typeof(object))
        {
            HandlerMappings = new Dictionary<string, ServiceOperationMapping>
            {
                ["First"] = new(typeof(FirstConcreteNotificationHandler), typeof(ConcreteNotification), typeof(Empty)),
                ["Second"] = new(typeof(SecondConcreteNotificationHandler), typeof(ConcreteNotification), typeof(Empty)),
                ["Base"] = new(typeof(BaseNotificationHandler), typeof(BaseNotification), typeof(Empty)),
                ["Failing"] = new(typeof(FailingNotificationHandler), typeof(FailingNotification), typeof(Empty)),
                ["Ordered"] = new(typeof(OrderedNotificationHandler), typeof(OrderedNotification), typeof(Empty)),
            }
        });
    }

    public sealed class NotificationOutboxHandler<T>(MemoryMessageClient client) : IOutboxHandler<T>
    {
        public async ValueTask HandleAsync(T message, OutboxDeliveryContext context, CancellationToken ct)
        {
            var topic = typeof(T) == typeof(ConcreteNotification) ? "test.concrete.v1"
                : typeof(T) == typeof(BaseNotification) ? "test.base.v1"
                : typeof(T) == typeof(OrderedNotification) ? "test.ordered.v1" : "test.failing.v1";
            await client.PublishAsync(topic, message, context, ct);
        }
    }

    private interface INotificationOperations : IApplicationService
    {
        Empty First(ConcreteNotification request);
        Empty Second(ConcreteNotification request);
        Empty Base(BaseNotification request);
        Empty Failing(FailingNotification request);
        Empty Ordered(OrderedNotification request);
    }

    private sealed class FirstConcreteNotificationHandler(NotificationDispatchProbe probe) : ServiceOperation<ConcreteNotification, Empty>
    {
        public override Task<Empty> HandleAsync(ConcreteNotification notification, InvocationContext context, CancellationToken cancellationToken)
        {
            probe.MarkFirstConcrete();
            return Task.FromResult(Empty.Instance);
        }
    }

    private sealed class SecondConcreteNotificationHandler(NotificationDispatchProbe probe) : ServiceOperation<ConcreteNotification, Empty>
    {
        public override Task<Empty> HandleAsync(ConcreteNotification notification, InvocationContext context, CancellationToken cancellationToken)
        {
            probe.MarkSecondConcrete();
            return Task.FromResult(Empty.Instance);
        }
    }

    private sealed class BaseNotificationHandler(NotificationDispatchProbe probe) : ServiceOperation<BaseNotification, Empty>
    {
        public override Task<Empty> HandleAsync(BaseNotification notification, InvocationContext context, CancellationToken cancellationToken)
        {
            probe.MarkBase();
            return Task.FromResult(Empty.Instance);
        }
    }

    private sealed class OrderedNotificationProbe
    {
        private readonly object _sync = new();
        private readonly List<int> _values = [];

        public IReadOnlyList<int> Values
        {
            get
            {
                lock (_sync)
                {
                    return [.. _values];
                }
            }
        }

        public void Add(int value)
        {
            lock (_sync)
            {
                _values.Add(value);
            }
        }
    }

    private sealed class OrderedNotificationHandler(OrderedNotificationProbe probe) : ServiceOperation<OrderedNotification, Empty>
    {
        public override Task<Empty> HandleAsync(OrderedNotification notification, InvocationContext context, CancellationToken cancellationToken)
        {
            probe.Add(notification.Value);
            return Task.FromResult(Empty.Instance);
        }
    }

    private sealed class FailingNotificationProbe
    {
        private int _attemptCount;

        public int AttemptCount => Volatile.Read(ref _attemptCount);

        public void MarkAttempt() => Interlocked.Increment(ref _attemptCount);
    }

    private sealed class FailingNotificationHandler(FailingNotificationProbe probe) : ServiceOperation<FailingNotification, Empty>
    {
        public override Task<Empty> HandleAsync(FailingNotification notification, InvocationContext context, CancellationToken cancellationToken)
        {
            probe.MarkAttempt();
            throw new InvalidOperationException("Notification failed.");
        }
    }

}
