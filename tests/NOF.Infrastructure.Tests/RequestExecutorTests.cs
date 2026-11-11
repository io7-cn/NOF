using Microsoft.Extensions.DependencyInjection;
using NOF.Abstraction;
using NOF.Application;
using NOF.Contract;
using Xunit;

namespace NOF.Infrastructure.Tests;

public class RequestExecutorTests
{
    [Fact]
    public async Task PlainDto_UsesScopedHandler_AndRestoresAmbientContext()
    {
        var services = new ServiceCollection();
        services.AddRequestHandler<Query, Reply, PlainHandler>();
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var first = provider.CreateScope();
        using var second = provider.CreateScope();
        var original = Context.Empty.WithTenantId("caller");
        using var ambient = Context.PushCurrent(original);
        var context = Context.Empty.WithTenantId("request");
        var firstReply = await Execute(first.ServiceProvider, context);
        var again = await Execute(first.ServiceProvider, context);
        var other = await Execute(second.ServiceProvider, context);
        Assert.Equal("request", firstReply.Tenant);
        Assert.Equal(firstReply.HandlerId, again.HandlerId);
        Assert.NotEqual(firstReply.HandlerId, other.HandlerId);
        Assert.Same(original, Context.Current);
    }

    [Fact]
    public async Task Policies_RunInRegistrationOrder_AndNormalizeContextBeforeHandlerConstruction()
    {
        var services = new ServiceCollection();
        services.AddSingleton<Trace>();
        services.AddRequestHandler<Query, Reply, PlainHandler>();
        services.AddScoped<IRequestMiddleware<Query, Reply>, NormalizeTenant>();
        services.AddScoped<IRequestMiddleware<Query, Reply>, ObserveTenant>();
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var result = await Execute(scope.ServiceProvider, Context.Empty);
        Assert.Equal("normalized", result.Tenant);
        Assert.Equal(new[] { "normalize:enter", "observe:normalized", "construct:normalized", "normalize:exit" },
            provider.GetRequiredService<Trace>().Entries);
    }

    [Fact]
    public async Task Rejection_DoesNotConstructHandlerOrItsDependencies()
    {
        var services = new ServiceCollection();
        services.AddNOFRequests();
        services.AddScoped<IRequestHandler<Query, Reply>>(_ => throw new InvalidOperationException("Handler was constructed"));
        services.AddScoped<IRequestMiddleware<Query, Reply>, Reject>();
        using var provider = services.BuildServiceProvider();
        var execution = await provider.GetRequiredService<IRequestExecutor>().ExecuteAsync<Query, Reply>(new(), Context.Empty);
        var rejected = Assert.IsType<RequestRejected>(execution.Value);
        Assert.Equal(RequestRejectionKind.Forbidden, rejected.Kind);
        Assert.Equal("access.denied", rejected.Failure.Code);
    }

    [Fact]
    public async Task NullBusinessResponse_IsACompletedExecution()
    {
        var services = new ServiceCollection();
        services.AddNOFRequests();
        services.AddScoped<IRequestHandler<Query, string?>>(_ => new DelegateHandler<string?>((_, _, _) => Task.FromResult<string?>(null)));
        using var provider = services.BuildServiceProvider();
        var execution = await provider.GetRequiredService<IRequestExecutor>().ExecuteAsync<Query, string?>(new(), Context.Empty);
        Assert.Null(Assert.IsType<RequestCompleted<string?>>(execution.Value).Response);
    }

    [Fact]
    public async Task BusinessFailure_IsACompletedExecution_NotAnExecutionRejection()
    {
        var services = new ServiceCollection();
        services.AddNOFRequests();
        services.AddScoped<IRequestHandler<Query, Result<string>>>(_ => new DelegateHandler<Result<string>>(
            (_, _, _) => Task.FromResult<Result<string>>(new Failure("order.closed", "Already closed"))));
        using var provider = services.BuildServiceProvider();
        var execution = await provider.GetRequiredService<IRequestExecutor>().ExecuteAsync<Query, Result<string>>(new(), Context.Empty);
        var response = Assert.IsType<RequestCompleted<Result<string>>>(execution.Value).Response;
        Assert.Equal("order.closed", Assert.IsType<Failure>(response.Value).Code);
    }

    [Fact]
    public async Task Cancellation_BeforeExecution_DoesNotConstructHandler()
    {
        var services = new ServiceCollection();
        services.AddNOFRequests();
        services.AddScoped<IRequestHandler<Query, Reply>>(_ => throw new InvalidOperationException("Handler was constructed"));
        using var provider = services.BuildServiceProvider();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => provider.GetRequiredService<IRequestExecutor>()
            .ExecuteAsync<Query, Reply>(new(), Context.Empty, cancellation.Token));
    }

    [Fact]
    public async Task CancellationToken_ReachesHandler_Unchanged()
    {
        using var cancellation = new CancellationTokenSource();
        var services = new ServiceCollection();
        services.AddNOFRequests();
        services.AddScoped<IRequestHandler<Query, string>>(_ => new DelegateHandler<string>((_, _, token) =>
        {
            Assert.Equal(cancellation.Token, token);
            cancellation.Cancel();
            return Task.FromCanceled<string>(token);
        }));
        using var provider = services.BuildServiceProvider();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => provider.GetRequiredService<IRequestExecutor>()
            .ExecuteAsync<Query, string>(new(), Context.Empty, cancellation.Token));
    }

    [Fact]
    public async Task HandlerException_Propagates_AndRestoresAmbientContext()
    {
        var original = Context.Empty.WithTenantId("caller");
        using var ambient = Context.PushCurrent(original);
        var failure = new InvalidOperationException("broken handler");
        var services = new ServiceCollection();
        services.AddNOFRequests();
        services.AddScoped<IRequestHandler<Query, string>>(_ => new DelegateHandler<string>(async (_, context, _) =>
        {
            await Task.Yield();
            Assert.Same(context, Context.Current);
            throw failure;
        }));
        using var provider = services.BuildServiceProvider();
        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() => provider.GetRequiredService<IRequestExecutor>()
            .ExecuteAsync<Query, string>(new(), Context.Empty));
        Assert.Same(failure, thrown);
        Assert.Same(original, Context.Current);
    }

    [Fact]
    public async Task UninitializedPolicyOutcome_FailsClosed()
    {
        var services = new ServiceCollection();
        services.AddNOFRequests();
        services.AddScoped<IRequestMiddleware<Query, Reply>, InvalidPolicy>();
        using var provider = services.BuildServiceProvider();
        await Assert.ThrowsAsync<InvalidOperationException>(() => provider.GetRequiredService<IRequestExecutor>()
            .ExecuteAsync<Query, Reply>(new(), Context.Empty));
    }

    [Fact]
    public void Registration_RejectsDuplicateHandlers_AndOnlyAddsExecutionServices()
    {
        var services = new ServiceCollection();
        services.AddNOFRequests().AddNOFRequests();
        Assert.Single(services);
        services.AddRequestHandler<Query, Reply, PlainHandler>();
        Assert.Throws<InvalidOperationException>(() => services.AddRequestHandler<Query, Reply, PlainHandler>());
    }

    private static async Task<Reply> Execute(IServiceProvider services, Context context)
    {
        var execution = await services.GetRequiredService<IRequestExecutor>().ExecuteAsync<Query, Reply>(new(), context);
        return Assert.IsType<RequestCompleted<Reply>>(execution.Value).Response;
    }

    public sealed record Query;
    public sealed record Reply(Guid HandlerId, string Tenant);
    public sealed class Trace
    {
        public List<string> Entries { get; } = [];
    }

    public sealed class PlainHandler : IRequestHandler<Query, Reply>
    {
        private readonly Guid _id = Guid.NewGuid();

        public PlainHandler(Trace? trace = null)
        {
            trace?.Entries.Add("construct:" + Context.Current.TenantId);
        }

        public async Task<Reply> HandleAsync(Query request, Context context, CancellationToken cancellationToken)
        {
            await Task.Yield();
            Assert.Same(context, Context.Current);
            return new Reply(_id, context.TenantId);
        }
    }

    public sealed class NormalizeTenant(Trace trace) : IRequestMiddleware<Query, Reply>
    {
        public async Task<RequestExecution<Reply>> InvokeAsync(Query request, Context context,
            RequestExecutionDelegate<Query, Reply> next, CancellationToken cancellationToken)
        {
            trace.Entries.Add("normalize:enter");
            var result = await next(request, context.WithTenantId("normalized"), cancellationToken);
            Assert.Same(context, Context.Current);
            trace.Entries.Add("normalize:exit");
            return result;
        }
    }

    public sealed class ObserveTenant(Trace trace) : IRequestMiddleware<Query, Reply>
    {
        public Task<RequestExecution<Reply>> InvokeAsync(Query request, Context context,
            RequestExecutionDelegate<Query, Reply> next, CancellationToken cancellationToken)
        {
            Assert.Same(context, Context.Current);
            trace.Entries.Add("observe:" + context.TenantId);
            return next(request, context, cancellationToken);
        }
    }

    public sealed class Reject : IRequestMiddleware<Query, Reply>
    {
        public Task<RequestExecution<Reply>> InvokeAsync(Query request, Context context,
            RequestExecutionDelegate<Query, Reply> next, CancellationToken cancellationToken)
            => Task.FromResult<RequestExecution<Reply>>(new RequestRejected(RequestRejectionKind.Forbidden,
                new Failure("access.denied", "Access denied")));
    }

    public sealed class InvalidPolicy : IRequestMiddleware<Query, Reply>
    {
        public Task<RequestExecution<Reply>> InvokeAsync(Query request, Context context,
            RequestExecutionDelegate<Query, Reply> next, CancellationToken cancellationToken)
            => Task.FromResult(default(RequestExecution<Reply>));
    }

    public sealed class DelegateHandler<TResponse>(Func<Query, Context, CancellationToken, Task<TResponse>> handle)
        : IRequestHandler<Query, TResponse>
    {
        public Task<TResponse> HandleAsync(Query request, Context context, CancellationToken cancellationToken)
            => handle(request, context, cancellationToken);
    }
}
