using NOF.Application;
using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace NOF.Infrastructure;

/// <summary>Records application execution metrics through the native Meter API.</summary>
public sealed class InstrumentationInvocationFilter : IInvocationFilter
{
    private static readonly Counter<long> Count = NOFInfrastructureConstants.InboundPipeline.Meter.CreateCounter<long>(NOFInfrastructureConstants.InboundPipeline.Metrics.ExecutionCounter);
    private static readonly Histogram<double> Duration = NOFInfrastructureConstants.InboundPipeline.Meter.CreateHistogram<double>(NOFInfrastructureConstants.InboundPipeline.Metrics.ExecutionDuration, "ms");
    private static readonly Counter<long> Errors = NOFInfrastructureConstants.InboundPipeline.Meter.CreateCounter<long>(NOFInfrastructureConstants.InboundPipeline.Metrics.ErrorCounter);

    /// <inheritdoc />
    public async ValueTask InvokeAsync(InvocationContext context, object input, InvocationDelegate next, CancellationToken cancellationToken)
    {
        var tags = new TagList
        {
            { NOFInfrastructureConstants.InboundPipeline.Tags.HandlerType, context.HandlerType.FullName },
            { NOFInfrastructureConstants.InboundPipeline.Tags.MessageType, context.OperationName }
        };
        var start = Stopwatch.GetTimestamp();
        try
        {
            await next(context, input, cancellationToken);
            Count.Add(1, tags);
        }
        catch
        {
            Errors.Add(1, tags);
            throw;
        }
        finally
        {
            Duration.Record(Stopwatch.GetElapsedTime(start).TotalMilliseconds, tags);
        }
    }
}
