using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Moq;
using NOF.Abstraction;
using NOF.Application;
using NOF.Contract;
using NOF.Hosting;
using Xunit;

namespace NOF.Infrastructure.Tests;

public sealed class MessageCapabilityRegistrationTests
{
    private static readonly Type[] WorkerTypes = [typeof(InboxMessageBackgroundService), typeof(InboxCleanupBackgroundService), typeof(OutboxMessageBackgroundService), typeof(OutboxCleanupBackgroundService)];
    [Fact]
    public void AddNOF_AloneDoesNotInstallDurableStoresModelsOrWorkers()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.AddNOF();
        using var host = builder.Build();
        Assert.DoesNotContain(host.Services.GetServices<IHostedService>(), service => WorkerTypes.Contains(service.GetType()));
        Assert.Empty(host.Services.GetServices<IDbContextModelCreatingContributor>());
        using var scope = host.Services.CreateScope();
        Assert.Null(scope.ServiceProvider.GetService<IOutbox>());
        Assert.Null(scope.ServiceProvider.GetService<IInboxStore>());
        Assert.Null(scope.ServiceProvider.GetService<IOutboxStore>());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void OutboxAssembly_IsIdempotentAndIndependentOfDefaultRegistrationOrder(bool capabilityFirst)
    {
        var builder = Host.CreateApplicationBuilder();
        if (capabilityFirst)
        {
            builder.Services.AddOutbox(_ =>
            {
            });
        }

        builder.AddNOF();
        builder.Services.AddOutbox(_ =>
        {
        });
        using var host = builder.Build();
        var workers = host.Services.GetServices<IHostedService>().Where(service => WorkerTypes.Contains(service.GetType())).ToArray();
        Assert.Equal(2, workers.Length);
        Assert.Contains(workers, service => service is OutboxMessageBackgroundService);
        Assert.Contains(workers, service => service is OutboxCleanupBackgroundService);
        Assert.Single(host.Services.GetServices<IDbContextModelCreatingContributor>());
        using var scope = host.Services.CreateScope();
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IOutbox>());
        Assert.Null(scope.ServiceProvider.GetService<IInboxStore>());
    }

    [Fact]
    public async Task DisabledWorkers_DoNotTouchCustomStoresOrValidateUnusedSchedulingOptions()
    {
        var inbox = new Mock<IInboxStore>(MockBehavior.Strict);
        var outbox = new Mock<IOutboxStore>(MockBehavior.Strict);
        var builder = Host.CreateApplicationBuilder();
        builder.AddNOF();
        builder.Services.AddSingleton(inbox.Object);
        builder.Services.AddSingleton(outbox.Object);
        builder.Services.AddOutbox(_ =>
        {
        });
        builder.Services.AddInbox();
        builder.Services.Configure<TransactionalMessageOptions>(options =>
        {
            options.Inbox.RunWorkers = options.Outbox.RunWorkers = false;
            options.Inbox.PollingInterval = options.Outbox.PollingInterval = TimeSpan.Zero;
        });
        using var host = builder.Build();
        await host.StartAsync();
        try
        {
            var workers = host.Services.GetServices<IHostedService>().Where(service => WorkerTypes.Contains(service.GetType())).Cast<BackgroundService>().ToArray();
            Assert.Equal(4, workers.Length);
            await Task.WhenAll(workers.Select(worker => worker.ExecuteTask!)).WaitAsync(TimeSpan.FromSeconds(5));
            inbox.VerifyNoOtherCalls();
            outbox.VerifyNoOtherCalls();
        }
        finally
        {
            await host.StopAsync();
        }
    }

    [Fact]
    public async Task ProducerOnlyHost_StillStagesAndCommitsIntentsWithoutConstructingHandlers()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.AddNOF();
        builder.Services.AddOutbox(outbox => outbox.Add<Work, Handler>("work"));
        builder.Services.AddSingleton<Probe>();
        builder.Services.Configure<TransactionalMessageOptions>(options => options.Outbox.RunWorkers = false);
        using var host = builder.Build();
        await host.StartAsync();
        try
        {
            using var scope = host.Services.CreateScope();
            var id = await scope.ServiceProvider.GetRequiredService<IOutbox>().EnqueueAsync(new Work("pending"), Context.Empty);
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
            using var observer = host.Services.CreateScope();
            var record = await observer.ServiceProvider.GetRequiredService<IPersistenceSession>().Set<NOFOutboxMessage>().SingleAsync(item => item.Id == id);
            Assert.Equal(OutboxMessageStatus.Pending, record.Status);
            Assert.Equal(0, record.RetryCount);
            Assert.Equal(0, host.Services.GetRequiredService<Probe>().Constructed);
        }
        finally
        {
            await host.StopAsync();
        }
    }

    [Fact]
    public async Task DisabledInboxWorkers_StillAcceptDurableReceipts()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.AddNOF();
        builder.Services.AddMemoryMessaging(bindings => bindings.MapQueue<Work>("work", new ServiceOperationDescriptor<Work, Empty>(typeof(IConsumer), nameof(IConsumer.Handle))));
        builder.Services.Configure<TransactionalMessageOptions>(options => options.Inbox.RunWorkers = false);
        using var host = builder.Build();
        await host.StartAsync();
        try
        {
            using var scope = host.Services.CreateScope();
            await scope.ServiceProvider.GetRequiredService<MemoryMessageClient>().SendAsync("work", new Work("pending"), Context.Empty);
            using var observer = host.Services.CreateScope();
            var record = await observer.ServiceProvider.GetRequiredService<IPersistenceSession>().Set<NOFInboxMessage>().SingleAsync();
            Assert.Equal(InboxMessageStatus.Pending, record.Status);
            Assert.Null(observer.ServiceProvider.GetService<IOutboxStore>());
        }
        finally
        {
            await host.StopAsync();
        }
    }

    [Fact]
    public async Task UnusedInboxOptions_DoNotBlockAnOutboxOnlyHost()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.AddNOF();
        builder.Services.AddOutbox(_ =>
        {
        });
        builder.Services.Configure<TransactionalMessageOptions>(options =>
        {
            options.Inbox.BatchSize = 0;
            options.Outbox.RunWorkers = false;
        });
        using var host = builder.Build();
        await host.StartAsync();
        await host.StopAsync();
    }

    public sealed record Work(string Value);
    public interface IConsumer : IApplicationService
    {
        Empty Handle(Work work);
    }

    public sealed class Probe
    {
        public int Constructed { get; set; }
    }

    public sealed class Handler : IOutboxHandler<Work>
    {
        public Handler(Probe probe) => probe.Constructed++;
        public ValueTask HandleAsync(Work message, OutboxDeliveryContext context, CancellationToken cancellationToken) => throw new InvalidOperationException("Workers are disabled.");
    }
}
