using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NOF.Application;
using NOF.Contract;
using NOF.Hosting;
using System.Diagnostics;

namespace NOF.Infrastructure;

/// <summary>Uses native Activity tracing; exception presentation belongs to the host.</summary>
public sealed class TracingInvocationFilter(IOptions<DeploymentOptions> deployment, ILogger<TracingInvocationFilter> logger) : IInvocationFilter
{
    /// <inheritdoc />
    public async ValueTask<object?> InvokeAsync(InvocationContext context, object input, InvocationDelegate next, CancellationToken cancellationToken)
    {
        using var activity = NOFInfrastructureConstants.InboundPipeline.Source.StartActivity(
            $"{context.HandlerType.FullName}.Handle: {context.OperationName}",
            context.Origin == InvocationOrigin.Message ? ActivityKind.Consumer : ActivityKind.Internal);
        activity?.SetServiceDeploymentTags(deployment.Value);
        activity?.SetTag(NOFInfrastructureConstants.InboundPipeline.Tags.HandlerType, context.HandlerType.FullName);
        activity?.SetTag(NOFInfrastructureConstants.InboundPipeline.Tags.MessageType, context.OperationName);
        try
        {
            var result = await next(context, input, cancellationToken);
            activity?.SetStatus(ActivityStatusCode.Ok);
            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception error)
        {
            activity?.SetStatus(ActivityStatusCode.Error, error.Message);
            activity?.AddException(error);
            logger.LogError(error, "Invocation {Operation} failed", context.OperationName);
            throw;
        }
    }
}
