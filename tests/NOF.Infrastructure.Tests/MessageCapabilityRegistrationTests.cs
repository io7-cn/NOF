using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Moq;
using NOF.Abstraction;
using NOF.Application;
using NOF.Contract;
using NOF.Hosting;
using NOF.Infrastructure.EntityFrameworkCore;
using Xunit;

namespace NOF.Infrastructure.Tests;

public sealed class MessageCapabilityRegistrationTests
{
    private static readonly Type[] WorkerTypes = [typeof(OutboxMessageBackgroundService), typeof(OutboxCleanupBackgroundService)];
    [Fact]
    public void AddNOF_AloneDoesNotInstallDurableStoresModelsOrWorkers()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.AddNOF();
        using var host = builder.Build();
        Assert.DoesNotContain(host.Services.GetServices<IHostedService>(), service => WorkerTypes.Contains(service.GetType()));
        Assert.Empty(host.Services.GetServices<IEFCoreModelConfiguration>());
        using var scope = host.Services.CreateScope();
        Assert.Null(scope.ServiceProvider.GetService<IOutbox>());
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
            builder.Services.AddOutbox();
        }

        builder.AddNOF();
        builder.AddNativeTestPersistence();
        builder.Services.AddOutbox();
        using var host = builder.Build();
        var workers = host.Services.GetServices<IHostedService>().Where(service => WorkerTypes.Contains(service.GetType())).ToArray();
        Assert.Equal(2, workers.Length);
        Assert.Contains(workers, service => service is OutboxMessageBackgroundService);
        Assert.Contains(workers, service => service is OutboxCleanupBackgroundService);
        using (var modelScope = host.Services.CreateScope())
        {
            var model = modelScope.ServiceProvider.GetRequiredService<DbContext>().Model;
            Assert.NotNull(model.FindEntityType(typeof(NOFOutboxMessage)));
        }
        using var scope = host.Services.CreateScope();
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IOutbox>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IOutboxStore>());
    }

    [Fact]
    public async Task ProducerOnlyHost_StillStagesAndCommitsIntentsWithoutConstructingHandlers()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.AddNOF();
        builder.AddNativeTestPersistence();
        builder.Services.AddOutbox().AddOutboxHandler<Work, Handler>();
        builder.Services.AddSingleton<Probe>();
        builder.Services.Configure<OutboxOptions>(options => options.RunWorkers = false);
        using var host = builder.Build();
        await host.StartAsync();
        try
        {
            using var scope = host.Services.CreateScope();
            var id = await scope.ServiceProvider.GetRequiredService<IOutbox>().EnqueueAsync(new Work("pending"), Context.Empty);
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
            using var observer = host.Services.CreateScope();
            var record = await observer.ServiceProvider.GetRequiredService<DbContext>().Set<NOFOutboxMessage>().SingleAsync(item => item.Id == id);
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
    public async Task DisabledOutboxWorkers_PreserveCustomStore_AndIgnoreUnusedSchedulingOptions()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.AddNOF();
        var store = new Mock<IOutboxStore>(MockBehavior.Strict);
        builder.Services.AddSingleton(store.Object);
        builder.Services.AddOutbox();
        builder.Services.Configure<OutboxOptions>(options =>
        {
            options.RunWorkers = false;
            options.PollingInterval = TimeSpan.Zero;
        });
        using var host = builder.Build();
        await host.StartAsync();
        try
        {
            using var scope = host.Services.CreateScope();
            Assert.Same(store.Object, scope.ServiceProvider.GetRequiredService<IOutboxStore>());
            var workers = host.Services.GetServices<IHostedService>().Where(service => WorkerTypes.Contains(service.GetType())).Cast<BackgroundService>();
            await Task.WhenAll(workers.Select(worker => worker.ExecuteTask!)).WaitAsync(TimeSpan.FromSeconds(5));
            store.VerifyNoOtherCalls();
        }
        finally { await host.StopAsync(); }
    }

    [Fact]
    public void RunningOutboxWorkers_ValidateSchedulingOptions()
    {
        using var services = new ServiceCollection().AddOutbox()
            .Configure<OutboxOptions>(options => options.BatchSize = 0).BuildServiceProvider();
        Assert.Throws<OptionsValidationException>(() => services.GetRequiredService<IOptions<OutboxOptions>>().Value);
    }

    public sealed record Work(string Value);
    public interface IConsumer : IApplicationService
    {
        void Handle(Work work);
    }

    public sealed class Probe
    {
        public int Constructed { get; set; }
    }

    public sealed class Handler : OutboxHandler<Work>
    {
        public Handler(Probe probe) => probe.Constructed++;
        public override ValueTask HandleAsync(Work message, OutboxDeliveryContext context, CancellationToken cancellationToken) => throw new InvalidOperationException("Workers are disabled.");
    }
}
