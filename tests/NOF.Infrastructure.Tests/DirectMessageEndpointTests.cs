using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NOF.Abstraction;
using NOF.Application;
using NOF.Hosting;
using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using Xunit;

namespace NOF.Infrastructure.Tests;

public sealed class DirectMessageEndpointTests
{
    [Fact]
    public async Task Endpoints_ExecuteWithoutPersistence_AndDoNotDeduplicateDeliveries()
    {
        using var host = CreateHost();
        Assert.Null(host.Services.GetService<IUnitOfWork>());
        Assert.Null(host.Services.GetService<IOutbox>());
        Assert.Null(host.Services.GetService<IOutboxStore>());
        using var scope = host.Services.CreateScope();
        var client = scope.ServiceProvider.GetRequiredService<MemoryMessageClient>();
        var context = new OutboxDeliveryContext
        {
            MessageId = Guid.NewGuid(),
            Context = Context.Empty,
            Attempt = 1,
            Headers = new Dictionary<string, string?> { [NOFAbstractionConstants.Transport.Headers.MessageId] = "same-delivery" }
        };
        await client.SendAsync("work", new Work("first"), context);
        await client.SendAsync("work", new Work("first"), context);
        var calls = host.Services.GetRequiredService<Probe>().Calls.ToArray();
        Assert.Equal(2, calls.Length);
        Assert.Equal(2, calls.Select(call => call.Scope).Distinct().Count());
        Assert.All(calls, call => Assert.Equal("same-delivery", call.Items[NOFAbstractionConstants.Transport.Headers.MessageId]!.GetValue<string>()));
    }

    [Theory]
    [InlineData(true, "explicit")]
    [InlineData(false, "ambient")]
    public async Task Context_IsBoundBeforeConstruction_AndCallerStateIsRestored(bool explicitTenant, string expectedTenant)
    {
        using var host = CreateHost();
        using var ambient = Context.PushCurrent(Context.Empty.WithTenantId("ambient").WithItem("caller", "unchanged"));
        using var scope = host.Services.CreateScope();
        var context = Context.Empty.WithItem("correlation", "original").WithItem("number", 7)
            .WithItem("enabled", true).WithItem("empty", null);
        if (explicitTenant)
        {
            context = context.WithTenantId("explicit");
        }
        await scope.ServiceProvider.GetRequiredService<MemoryMessageClient>().SendAsync("work", new Work("context"), context);
        var call = Assert.Single(host.Services.GetRequiredService<Probe>().Calls);
        Assert.Equal(expectedTenant, call.TenantAtConstruction);
        Assert.Equal("original", call.CorrelationAtConstruction);
        Assert.Equal(7, call.Items["number"]!.GetValue<int>());
        Assert.True(call.Items["enabled"]!.GetValue<bool>());
        Assert.True(call.Items.ContainsKey("empty"));
        Assert.Null(call.Items["empty"]);
        Assert.Equal(InvocationOrigin.Message, call.Origin);
        Assert.Equal("ambient", Context.Current.TenantId);
        Assert.Equal("unchanged", Context.Current["caller"]!.GetValue<string>());
    }

    [Fact]
    public async Task SendAsync_WaitsForHandlerCompletion_AndPropagatesFailures()
    {
        using var host = CreateHost();
        using var scope = host.Services.CreateScope();
        var probe = host.Services.GetRequiredService<Probe>();
        var client = scope.ServiceProvider.GetRequiredService<MemoryMessageClient>();
        var sending = client.SendAsync("work", new Work("wait"), Context.Empty);
        await probe.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(sending.IsCompleted);
        probe.Release.SetResult();
        await sending.WaitAsync(TimeSpan.FromSeconds(5));
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => client.SendAsync("work", new Work("fail"), Context.Empty));
        Assert.Same(probe.Failure, error);
    }

    [Fact]
    public async Task PublishAsync_InvokesEachSubscriptionDirectly()
    {
        using var host = CreateHost();
        using var scope = host.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<MemoryMessageClient>().PublishAsync("facts", new Work("fanout"), Context.Empty);
        var calls = host.Services.GetRequiredService<Probe>().Calls.ToArray();
        Assert.Equal(2, calls.Length);
        Assert.Equal(2, calls.Select(call => call.Scope).Distinct().Count());
    }

    [Fact]
    public async Task Cancellation_ReachesTheRunningHandler()
    {
        using var host = CreateHost();
        using var scope = host.Services.CreateScope();
        using var cancellation = new CancellationTokenSource();
        var sending = scope.ServiceProvider.GetRequiredService<MemoryMessageClient>()
            .SendAsync("work", new Work("wait"), Context.Empty, cancellation.Token);
        await host.Services.GetRequiredService<Probe>().Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => sending);
    }

    [Fact]
    public async Task FilterRejection_IsNotReportedAsSuccessfulDelivery_AndDoesNotConstructHandler()
    {
        using var host = CreateHost(services => services.AddInvocationFilters(filters => filters.Global.Use<Reject>()));
        using var scope = host.Services.CreateScope();
        await Assert.ThrowsAsync<InvalidOperationException>(() => scope.ServiceProvider.GetRequiredService<MemoryMessageClient>()
            .SendAsync("work", new Work("rejected"), Context.Empty));
        Assert.Empty(host.Services.GetRequiredService<Probe>().Calls);
        Assert.Equal(0, host.Services.GetRequiredService<Probe>().Constructed);
    }

    [Fact]
    public async Task TransportMetadata_OverridesConflictingContextSnapshot()
    {
        using var host = CreateHost();
        var headers = new Dictionary<string, string?>
        {
            [NOFAbstractionConstants.Transport.Headers.TenantId.ToUpperInvariant()] = "actual",
            [NOFAbstractionConstants.Transport.Headers.MessageId] = "native-id",
            [NOFAbstractionConstants.Transport.Headers.Context] = new JsonObject
            {
                [NOFAbstractionConstants.Transport.Headers.TenantId] = "forged",
                [NOFAbstractionConstants.Transport.Headers.MessageId] = "forged-id"
            }.ToJsonString()
        };
        await host.Services.ExecuteMessageAsync(Descriptor, Payload(host, "context"), headers);
        var call = Assert.Single(host.Services.GetRequiredService<Probe>().Calls);
        Assert.Equal("actual", call.TenantAtConstruction);
        Assert.Equal("native-id", call.Items[NOFAbstractionConstants.Transport.Headers.MessageId]!.GetValue<string>());
    }

    [Theory]
    [InlineData("{broken")]
    [InlineData("null")]
    public async Task InvalidPayload_FailsWithoutConstructingHandler(string json)
    {
        using var host = CreateHost();
        await Assert.ThrowsAnyAsync<Exception>(() => host.Services.ExecuteMessageAsync(Descriptor,
            System.Text.Encoding.UTF8.GetBytes(json), null));
        Assert.Equal(0, host.Services.GetRequiredService<Probe>().Constructed);
    }

    private static ReadOnlyMemory<byte> Payload(IHost host, string text)
        => host.Services.GetRequiredService<IObjectSerializer>().Serialize(new Work(text), typeof(Work));
    private static OperationDescriptor<Work> Descriptor => new(typeof(Receive));
    private static IHost CreateHost(Action<IServiceCollection>? configure = null)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.AddNOF();
        builder.Services.AddSingleton<Probe>();
        builder.Services.AddScoped<Receive>();
        builder.Services.GetOrAddSingleton<ApplicationServiceRegistry>().AddOperation(
            new(typeof(Receive), typeof(Work), typeof(void)) { Descriptor = Descriptor });
        builder.Services.AddMemoryMessaging(endpoints =>
        {
            endpoints.MapQueue<Receive>("work");
            endpoints.MapSubscription<Receive>("facts", "first");
            endpoints.MapSubscription<Receive>("facts", "second");
        });
        configure?.Invoke(builder.Services);
        return builder.Build();
    }

    public sealed record Work(string Text);
    public sealed record Call(Guid Scope, string TenantAtConstruction, string? CorrelationAtConstruction, JsonObject Items, InvocationOrigin Origin);
    public sealed class Probe
    {
        public int Constructed;
        public ConcurrentBag<Call> Calls { get; } = [];
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public InvalidOperationException Failure { get; } = new("business failure");
    }
    public sealed class Receive : OperationHandler<Work>
    {
        private readonly Probe _probe;
        private readonly string _tenant = Context.Current.TenantId;
        private readonly string? _correlation = Context.Current["correlation"]?.GetValue<string>();
        private readonly Guid _scope = Guid.NewGuid();
        public Receive(Probe probe)
        {
            _probe = probe;
            Interlocked.Increment(ref probe.Constructed);
        }
        public override async Task HandleAsync(Work request, InvocationContext context, CancellationToken cancellationToken)
        {
            _probe.Calls.Add(new(_scope, _tenant, _correlation, context.Items, context.Origin));
            if (request.Text == "fail")
            {
                throw _probe.Failure;
            }
            if (request.Text == "wait")
            {
                _probe.Started.TrySetResult();
                await _probe.Release.Task.WaitAsync(cancellationToken);
            }
        }
    }
    public sealed record Denied(string Title) : IFailure { public string Detail => "Rejected"; }
    public sealed class Reject : IInvocationFilter
    {
        public static IReadOnlyList<InvocationFailureCase> FailureCases => [new("denied", typeof(Denied), typeof(Denied), "Denied", value => value, _ => "Rejected")];
        public ValueTask<object?> InvokeAsync(InvocationContext context, object input, InvocationDelegate next, CancellationToken cancellationToken)
            => ValueTask.FromResult<object?>(new Denied("Denied"));
    }
}
