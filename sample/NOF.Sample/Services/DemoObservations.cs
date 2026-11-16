using NOF.Abstraction;
using NOF.Application;
using NOF.Infrastructure;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace NOF.Sample.Services;

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
