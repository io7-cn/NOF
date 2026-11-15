using NOF.Abstraction;
using NOF.Application;
using NOF.Infrastructure;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace NOF.Sample.Services;

public sealed record DemoEvent(string Text, DateTimeOffset PublishedAt);
public sealed record DemoInvocation(string Operation, string Origin, string Tenant, double Milliseconds, string Outcome, string? TraceId);

public sealed class DemoObservations
{
    public ConcurrentQueue<DemoInvocation> Invocations { get; } = new();
    public ConcurrentQueue<DemoEvent> Events { get; } = new();
    public void Add(DemoInvocation invocation)
    {
        Invocations.Enqueue(invocation);
        while (Invocations.Count > 30)
        {
            Invocations.TryDequeue(out _);
        }
    }
}

public sealed class DemoInvocationFilter(DemoObservations observations) : IInvocationFilter
{
    public async ValueTask<object?> InvokeAsync(InvocationContext context, object input, InvocationDelegate next, CancellationToken cancellationToken)
    {
        var started = Stopwatch.GetTimestamp();
        var outcome = "完成";
        try
        {
            return await next(context, input, cancellationToken);
        }
        catch
        {
            outcome = "异常";
            throw;
        }
        finally
        {
            observations.Add(new(context.Operation.Id.ToString(), context.Origin.ToString(), context.Context.TenantId,
                Stopwatch.GetElapsedTime(started).TotalMilliseconds, outcome, Activity.Current?.TraceId.ToString()));
        }
    }
}

public sealed class ObserveDemoEvent(DemoObservations observations) : LocalEventHandler<DemoEvent>
{
    public override Task HandleAsync(DemoEvent @event, Context context, CancellationToken cancellationToken)
    {
        observations.Events.Enqueue(@event);
        while (observations.Events.Count > 20)
        {
            observations.Events.TryDequeue(out _);
        }
        return Task.CompletedTask;
    }
}
