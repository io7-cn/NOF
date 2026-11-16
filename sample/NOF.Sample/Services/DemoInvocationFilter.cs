using NOF.Abstraction;
using NOF.Application;
using NOF.Infrastructure;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace NOF.Sample.Services;

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
