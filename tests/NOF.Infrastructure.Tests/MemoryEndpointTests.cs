using Microsoft.Extensions.DependencyInjection;
using NOF.Abstraction;
using NOF.Application;
using NOF.Contract;
using Xunit;

namespace NOF.Infrastructure.Tests;

public sealed class MemoryEndpointTests
{
    [Fact]
    public void EmptySuccessWithDeclaredFailures_CannotBeSubmittedOrBoundAsEmpty()
    {
        var registry = new MemoryEndpointRegistry();
        var bindings = new MemoryEndpointsBuilder(new ServiceCollection(), registry);
        var reference = new ServiceOperationReference<Message>(typeof(IService), nameof(IService.MayFail));
        Assert.False(ServiceOperationInspector.CanSubmit(reference));
        // Even a manually forged success-only descriptor must not bypass the Contract declaration.
        var descriptor = new ServiceOperationDescriptor<Message, Empty>(typeof(IService), nameof(IService.MayFail));
        Assert.Throws<InvalidOperationException>(() => bindings.MapQueue<Message>("invalid.v1", descriptor));
        Assert.Throws<InvalidOperationException>(() => bindings.MapSubscription<Message>("facts.v1", "invalid", descriptor));
    }
    private static ServiceOperationDescriptor<Message, Empty> First => new(typeof(IService), nameof(IService.First));
    private static ServiceOperationDescriptor<Message, Empty> Second => new(typeof(IService), nameof(IService.Second));

    [Fact]
    public void SharedInput_DifferentOperationsAndSubscriptions_HaveIndependentIdentities()
    {
        var registry = new MemoryEndpointRegistry();
        var bindings = new MemoryEndpointsBuilder(new ServiceCollection(), registry);
        bindings.MapQueue<Message>("first.v1", First);
        bindings.MapQueue<Message>("second.v1", Second);
        bindings.MapSubscription<Message>("facts.v1", "projection", First);
        bindings.MapSubscription<Message>("facts.v1", "audit", Second);
        Assert.Equal("first.v1", registry.GetConsumer("memory:queue:first.v1").Route);
        Assert.Equal("second.v1", registry.GetConsumer("memory:queue:second.v1").Route);
        Assert.Equal(First.Id, registry.GetConsumer("memory:subscription:projection").Operation.Id);
        Assert.Equal(Second.Id, registry.GetConsumer("memory:subscription:audit").Operation.Id);
        Assert.Throws<InvalidOperationException>(() => bindings.MapQueue<Message>("late.v1", First));
    }

    [Fact]
    public void OneOperation_CanReceiveMultipleExplicitInboundQueues()
    {
        var registry = new MemoryEndpointRegistry();
        var bindings = new MemoryEndpointsBuilder(new ServiceCollection(), registry);
        bindings.MapQueue<Message>("first.v1", First);
        bindings.MapQueue<Message>("second.v1", First);
        Assert.Equal(2, registry.Endpoints.Count);
        Assert.All(registry.Endpoints, binding => Assert.Equal(First.Id, binding.Operation.Id));
        Assert.Null(typeof(MemoryEndpointsBuilder).GetMethod("RouteToQueue"));
        Assert.Null(typeof(MemoryEndpointsBuilder).GetMethod("Topic"));
    }

    [Fact]
    public void AmbiguousConsumerIds_AreRejected()
    {
        var registry = new MemoryEndpointRegistry();
        var bindings = new MemoryEndpointsBuilder(new ServiceCollection(), registry);
        bindings.MapSubscription<Message>("facts.v1", "same", First);
        Assert.Throws<InvalidOperationException>(() => bindings.MapSubscription<Message>("facts.v1", "same", Second));
    }

    [Fact]
    public void HostRegistries_AreIsolated()
    {
        var first = new ServiceCollection().AddMemoryMessaging(bindings => bindings.MapQueue<Message>("one.v1", First));
        var second = new ServiceCollection().AddMemoryMessaging(bindings => bindings.MapQueue<Message>("two.v1", First));
        using var one = first.BuildServiceProvider();
        using var two = second.BuildServiceProvider();
        Assert.Equal("one.v1", one.GetRequiredService<MemoryEndpointRegistry>().Endpoints.Single().Route);
        Assert.Equal("two.v1", two.GetRequiredService<MemoryEndpointRegistry>().Endpoints.Single().Route);
    }

    [Theory]
    [InlineData(InvocationOrigin.Local)]
    [InlineData(InvocationOrigin.Http)]
    [InlineData(InvocationOrigin.Message)]
    public async Task OperationPolicies_ApplyToEveryOrigin_BeforeHandlerConstruction(InvocationOrigin origin)
    {
        var services = new ServiceCollection().UseInMemoryInvocationPipeline();
        services.AddInvocationFilter<Reject>();
        services.AddScoped<Handler>(_ => throw new InvalidOperationException("Constructed a rejected handler"));
        using var provider = services.BuildServiceProvider();
        var registry = new ApplicationServiceRegistry();
        registry.Add(new(typeof(IService), typeof(object))
        {
            HandlerMappings = new Dictionary<string, ServiceOperationMapping> { [nameof(IService.First)] = new(typeof(Handler), typeof(Message), typeof(Empty)) }
        });
        var executor = new OperationExecutor(provider, new ApplicationServiceInvocationResolver(provider, registry));
        var result = await executor.ExecuteAsync(First, new Message(), Context.Empty, origin);
        Assert.Equal("403", Assert.IsType<Denied>(result).Title);
    }

    public interface IService : IApplicationService
    {
        Empty First(Message input);
        Empty Second(Message input);
        [FailureCase("Rejected")]
        Empty MayFail(Message input);
    }
    public sealed record Message;
    public sealed record Denied(string Title) : IFailure { public string Detail => "Denied"; }
    public sealed class Reject : IInvocationFilter
    {
        public static IReadOnlyList<InvocationFailureCase> FailureCases => [new("deny", typeof(Denied), typeof(Denied), "403", value => value, _ => "Denied")];
        public ValueTask<object?> InvokeAsync(InvocationContext context, object input, InvocationDelegate next, CancellationToken cancellationToken)
        {
            return ValueTask.FromResult<object?>(new Denied("403"));
        }
    }
    public sealed class Handler : ServiceOperation<Message, Empty>
    {
        public override Task<Empty> HandleAsync(Message input, InvocationContext context, CancellationToken cancellationToken) => Task.FromResult(Empty.Instance);
    }
}
