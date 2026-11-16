using NOF.Abstraction;
using NOF.Application;
using NOF.Infrastructure;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace NOF.Sample.Services;

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
