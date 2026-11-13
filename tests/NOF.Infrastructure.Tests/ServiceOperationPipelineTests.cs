using Microsoft.Extensions.DependencyInjection;
using NOF.Abstraction;
using NOF.Application;
using NOF.Contract;
using NOF.Hosting;
using Xunit;

namespace NOF.Infrastructure.Tests;

public class ServiceOperationPipelineTests
{
    [Fact]
    public async Task RejectedOperations_DoNotConstructHandlers()
    {
        var services = new ServiceCollection().UseInMemoryInvocationPipeline();
        services.AddScoped<ProbeOperation>(_ => throw new InvalidOperationException("Handler constructed"));
        services.AddScoped<IInvocationFilter, Deny>();
        using var provider = services.BuildServiceProvider();
        var result = await Execute(provider);
        Assert.Equal("403", Assert.IsType<ServiceRejected>(result.Execution.Value).Failure.Code);
        Assert.Null(result.Response);
    }

    [Fact]
    public async Task NormalizedContext_IsBoundBeforeDependencyResolution_AndRestoredAfterAwait()
    {
        using var ambient = Context.PushCurrent(Context.Empty.WithTenantId("caller"));
        var services = new ServiceCollection().UseInMemoryInvocationPipeline();
        services.AddScoped<ProbeOperation>();
        services.AddScoped<IInvocationFilter, Normalize>();
        using var provider = services.BuildServiceProvider();
        using var firstScope = provider.CreateScope();
        using var otherScope = provider.CreateScope();
        var first = await Execute(firstScope.ServiceProvider);
        var again = await Execute(firstScope.ServiceProvider);
        var other = await Execute(otherScope.ServiceProvider);
        var reply = first.Execution.Require<Reply>();
        Assert.Equal("normalized", reply.Tenant);
        Assert.Equal("normalized", reply.ConstructorTenant);
        Assert.Equal(reply.HandlerId, again.Execution.Require<Reply>().HandlerId);
        Assert.NotEqual(reply.HandlerId, other.Execution.Require<Reply>().HandlerId);
        Assert.Equal("caller", Context.Current.TenantId);
    }

    [Fact]
    public async Task Cancellation_DoesNotConstructHandlers()
    {
        var services = new ServiceCollection().UseInMemoryInvocationPipeline();
        services.AddScoped<ProbeOperation>(_ => throw new InvalidOperationException("Handler constructed"));
        using var provider = services.BuildServiceProvider();
        using var source = new CancellationTokenSource();
        source.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Execute(provider, source.Token));
    }

    [Fact]
    public async Task Exceptions_PropagateAndRestoreAmbientContext()
    {
        using var ambient = Context.PushCurrent(Context.Empty.WithTenantId("caller"));
        var services = new ServiceCollection().UseInMemoryInvocationPipeline();
        services.AddScoped<ThrowingOperation>();
        using var provider = services.BuildServiceProvider();
        var pipeline = CreateExecutor(provider, typeof(ThrowingOperation));
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await pipeline.ExecuteAsync(new ServiceOperationDescriptor<Query, Reply>(typeof(IProbeService), nameof(IProbeService.Read)), new Query(), Context.Empty, InvocationOrigin.Local));
        Assert.Equal("caller", Context.Current.TenantId);
    }

    [Fact]
    public async Task NullableCompletion_IsSeparateFromUninitializedExecution()
    {
        var services = new ServiceCollection().UseInMemoryInvocationPipeline();
        services.AddScoped<NullableOperation>();
        using var provider = services.BuildServiceProvider();
        var result = await CreateExecutor(provider, typeof(NullableOperation)).ExecuteAsync(new ServiceOperationDescriptor<Query, Reply?>(typeof(IProbeService), nameof(IProbeService.Find), true), new Query(), Context.Empty, InvocationOrigin.Local);
        Assert.IsType<ServiceCompleted>(result.Execution.Value);
        Assert.Null(result.Execution.Require<Reply?>(allowNull: true));
        Assert.Null(result.Rejection);
    }

    private static Task<InvocationContext> Execute(IServiceProvider provider, CancellationToken cancellationToken = default)
        => CreateExecutor(provider, typeof(ProbeOperation)).ExecuteAsync(new ServiceOperationDescriptor<Query, Reply>(typeof(IProbeService), nameof(IProbeService.Read)), new Query(), Context.Empty, InvocationOrigin.Local, cancellationToken);

    private static OperationExecutor CreateExecutor(IServiceProvider provider, Type implementation)
    {
        var registry = new ApplicationServiceRegistry();
        registry.Add(new(typeof(IProbeService), typeof(object))
        {
            HandlerMappings = new Dictionary<string, ServiceOperationMapping>
            {
                [nameof(IProbeService.Read)] = new(implementation, typeof(Query), typeof(Reply)),
                [nameof(IProbeService.Find)] = new(implementation, typeof(Query), typeof(Reply))
            }
        });
        return new(provider, new ApplicationServiceInvocationResolver(provider, registry));
    }

    public sealed record Query;
    public sealed record Reply(Guid HandlerId, string Tenant, string ConstructorTenant);
    public interface IProbeService : IApplicationService
    {
        Reply Read(Query request);
        Reply? Find(Query request);
    }

    public sealed class ProbeOperation : ServiceOperation<Query, Reply>
    {
        private readonly Guid _id = Guid.NewGuid();
        private readonly string _constructorTenant = Context.Current.TenantId;

        public override async Task<Reply> HandleAsync(Query request, InvocationContext context, CancellationToken cancellationToken)
        {
            await Task.Yield();
            Assert.Same(context.Context, Context.Current);
            return new Reply(_id, context.TenantId, _constructorTenant);
        }
    }

    public sealed class ThrowingOperation : ServiceOperation<Query, Reply>
    {
        public override async Task<Reply> HandleAsync(Query request, InvocationContext context, CancellationToken cancellationToken)
        {
            await Task.Yield();
            throw new InvalidOperationException("Expected");
        }
    }

    public sealed class NullableOperation : ServiceOperation<Query, Reply?>
    {
        public override bool AllowsNullResponse => true;
        public override Task<Reply?> HandleAsync(Query request, InvocationContext context, CancellationToken cancellationToken)
            => Task.FromResult<Reply?>(null);
    }

    public sealed class Deny : IInvocationFilter
    {
        public ValueTask InvokeAsync(InvocationContext context, object request, InvocationDelegate next, CancellationToken cancellationToken)
        {
            context.Reject(new Failure("403", "Forbidden"));
            return ValueTask.CompletedTask;
        }
    }

    public sealed class Normalize : IInvocationFilter
    {
        public ValueTask InvokeAsync(InvocationContext context, object request, InvocationDelegate next, CancellationToken cancellationToken)
            => next((InvocationContext)context.WithTenantId("normalized"), request, cancellationToken);
    }
}
