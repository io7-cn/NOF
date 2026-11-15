using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NOF.Abstraction;
using NOF.Application;
using NOF.Hosting;
using System.Text.Json.Nodes;
using Xunit;

namespace NOF.Infrastructure.Tests;

public sealed class TransactionalContextDeliveryTests
{
    [Fact]
    public async Task Outbox_ToInbox_PreservesJsonScalarsAndStableIdentity_WithoutManualReplay()
    {
        var builder = Host.CreateApplicationBuilder();
        builder.AddNOF();
        builder.AddNativeTestPersistence();
        var probe = new Probe();
        builder.Services.AddSingleton(probe);
        builder.Services.AddScoped<Receive>();
        var descriptor = new OperationDescriptor<Work>(typeof(Receive));
        builder.Services.GetOrAddSingleton<ApplicationServiceRegistry>()
            .AddOperation(new(typeof(Receive), typeof(Work), typeof(void)) { Descriptor = descriptor });
        builder.Services.AddMemoryMessaging(endpoints => endpoints.MapQueue<Receive>("work"));
        builder.Services.AddOutbox().AddOutboxHandler<Work, Forward>();
        builder.Services.Configure<TransactionalMessageOptions>(options =>
        {
            options.Outbox.PollingInterval = options.Inbox.PollingInterval = TimeSpan.FromMilliseconds(10);
        });
        using var host = builder.Build();
        var snapshot = new JsonObject { ["correlation"] = "original", ["number"] = 7, ["flag"] = true, ["empty"] = null };
        Guid id;
        using (var scope = host.Services.CreateScope())
        {
            id = await scope.ServiceProvider.GetRequiredService<IOutbox>().EnqueueAsync(new Work("payload"),
                Context.Empty.WithItems(snapshot), new Dictionary<string, string?> { ["custom"] = "header" });
            await scope.ServiceProvider.GetRequiredService<IUnitOfWork>().SaveChangesAsync();
        }
        snapshot["correlation"] = "mutated after commit";
        Assert.Null(probe.Received);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await host.StartAsync(timeout.Token);
        try
        {
            await probe.Completed.Task.WaitAsync(timeout.Token);
            Assert.Equal("payload", probe.Received);
            Assert.Equal("original", probe.Context!["correlation"]!.GetValue<string>());
            Assert.Equal(7, probe.Context["number"]!.GetValue<int>());
            Assert.True(probe.Context["flag"]!.GetValue<bool>());
            Assert.True(probe.Context.ContainsKey("empty"));
            Assert.Null(probe.Context["empty"]);
            Assert.Equal(id.ToString(), probe.Context[NOFAbstractionConstants.Transport.Headers.MessageId]!.GetValue<string>());
            Assert.Equal("header", probe.Context["custom"]!.GetValue<string>());
            Assert.Equal("original", probe.ConstructorCorrelation);
            using var scope = host.Services.CreateScope();
            var record = await scope.ServiceProvider.GetRequiredService<DbContext>().Set<NOFOutboxMessage>().SingleAsync(timeout.Token);
            Assert.Equal(id, record.Id);
            var headers = scope.ServiceProvider.GetRequiredService<IObjectSerializer>().Deserialize<Dictionary<string, string?>>(record.Headers)!;
            Assert.Equal(id.ToString(), headers[NOFAbstractionConstants.Transport.Headers.MessageId]);
        }
        finally
        {
            await host.StopAsync();
        }
    }

    public sealed record Work(string Text);
    public sealed class Probe
    {
        public string? Received;
        public string? ConstructorCorrelation;
        public JsonObject? Context;
        public TaskCompletionSource Completed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
    public sealed class Forward(MemoryMessageClient client) : OutboxHandler<Work>
    {
        public override ValueTask HandleAsync(Work message, OutboxDeliveryContext context, CancellationToken cancellationToken)
            => new(client.SendAsync("work", message, context, cancellationToken));
    }
    public sealed class Receive : OperationHandler<Work>
    {
        private readonly Probe _probe;
        public Receive(Probe probe)
        {
            _probe = probe;
            probe.ConstructorCorrelation = Context.Current["correlation"]?.GetValue<string>();
        }
        public override Task HandleAsync(Work request, InvocationContext context, CancellationToken cancellationToken)
        {
            _probe.Received = request.Text;
            _probe.Context = context.Items;
            _probe.Completed.TrySetResult();
            return Task.CompletedTask;
        }
    }
}
