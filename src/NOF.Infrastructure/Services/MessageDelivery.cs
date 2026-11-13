using Microsoft.Extensions.Hosting;
using NOF.Abstraction;
using NOF.Contract;
using NOF.Hosting;
using System.Diagnostics;

namespace NOF.Infrastructure;

/// <summary>Prepares reliable-delivery metadata and a native producer Activity around dispatch.</summary>
public sealed class MessageDelivery(IHostEnvironment hostEnvironment)
{
    /// <summary>Creates one message identity, tenant and trace envelope for sending or staging an outbox message.</summary>
    public async ValueTask ExecuteAsync(object message, Context context,
        Func<Dictionary<string, string?>, CancellationToken, ValueTask> dispatch, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var activity = NOFHostingConstants.Outbound.Source.StartActivity($"Outbound: {message.GetType().DisplayName}", ActivityKind.Producer);
        activity?.SetServiceDeploymentTags(hostEnvironment);
        var tenant = context.TryGetItem(NOFAbstractionConstants.Transport.Headers.TenantId, out var value) && value is string supplied
            ? supplied : Context.Current.TenantId;
        var headers = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            [NOFAbstractionConstants.Transport.Headers.MessageId] = Guid.NewGuid().ToString(),
            [NOFAbstractionConstants.Transport.Headers.TenantId] = TenantId.Normalize(tenant),
            [NOFAbstractionConstants.Transport.Headers.TraceParent] = Activity.Current?.ToTraceParent()
        };
        activity?.SetTag(NOFHostingConstants.Outbound.Tags.MessageId, headers[NOFAbstractionConstants.Transport.Headers.MessageId]);
        activity?.SetTag(NOFHostingConstants.Outbound.Tags.TenantId, headers[NOFAbstractionConstants.Transport.Headers.TenantId]);
        activity?.SetTag(NOFHostingConstants.Outbound.Tags.MessageType, message.GetType().Name);
        try
        {
            await dispatch(headers, cancellationToken).ConfigureAwait(false);
            activity?.SetStatus(ActivityStatusCode.Ok);
        }
        catch (Exception error)
        {
            activity?.SetStatus(ActivityStatusCode.Error, error.Message);
            throw;
        }
    }
}
