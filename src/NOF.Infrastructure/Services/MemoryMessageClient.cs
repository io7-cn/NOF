using NOF.Abstraction;
using NOF.Hosting;
using System.Diagnostics;

namespace NOF.Infrastructure;

/// <summary>Explicit Memory client; queue/topic selection belongs to its caller, not application operations.</summary>
public sealed class MemoryMessageClient(IObjectSerializer serializer, MemoryEndpointRegistry bindings, IServiceProvider services)
{
    /// <summary>Waits for the selected local operation to complete; failures propagate to the caller.</summary>
    public Task SendAsync<T>(string queue, T message, Context context, CancellationToken cancellationToken = default)
        => DispatchAsync(queue, message, context, false, null, cancellationToken);
    /// <summary>Publishes to a selected topic.</summary>
    public Task PublishAsync<T>(string topic, T message, Context context, CancellationToken cancellationToken = default)
        => DispatchAsync(topic, message, context, true, null, cancellationToken);
    /// <summary>Forwards an already prepared Outbox delivery, retaining its complete metadata.</summary>
    public Task SendAsync<T>(string queue, T message, OutboxDeliveryContext context, CancellationToken cancellationToken = default)
        => DispatchAsync(queue, message, context.Context, false, context, cancellationToken);
    /// <summary>Publishes an already prepared Outbox delivery, retaining its complete metadata.</summary>
    public Task PublishAsync<T>(string topic, T message, OutboxDeliveryContext context, CancellationToken cancellationToken = default)
        => DispatchAsync(topic, message, context.Context, true, context, cancellationToken);

    private async Task DispatchAsync<T>(string route, T message, Context context, bool publish,
        OutboxDeliveryContext? intent, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(route);
        ArgumentNullException.ThrowIfNull(message);
        ArgumentNullException.ThrowIfNull(context);
        cancellationToken.ThrowIfCancellationRequested();
        using var activity = NOFHostingConstants.Outbound.Source.StartActivity($"Memory: {typeof(T).Name}", ActivityKind.Producer);
        var tenant = context.TryGetItem(NOFAbstractionConstants.Transport.Headers.TenantId, out _)
            ? context.TenantId : Context.Current.TenantId;
        var headers = intent?.Headers ?? new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            [NOFAbstractionConstants.Transport.Headers.MessageId] = Guid.NewGuid().ToString(),
            [NOFAbstractionConstants.Transport.Headers.TenantId] = tenant,
            [NOFAbstractionConstants.Transport.Headers.TraceParent] = Activity.Current?.ToTraceParent(),
            [NOFAbstractionConstants.Transport.Headers.Context] = context.WithTenantId(tenant).Items.ToJsonString()
        };
        activity?.SetTag(NOFHostingConstants.Outbound.Tags.MessageId, headers[NOFAbstractionConstants.Transport.Headers.MessageId]);
        activity?.SetTag(NOFHostingConstants.Outbound.Tags.TenantId, tenant);
        activity?.SetTag(NOFHostingConstants.Outbound.Tags.MessageType, typeof(T).Name);
        try
        {
            var payload = serializer.Serialize(message, typeof(T));
            if (publish)
            {
                foreach (var binding in bindings.Endpoints.Where(binding => binding.IsSubscription && binding.Route == route))
                {
                    await services.ExecuteMessageAsync(binding.Operation, payload, headers, cancellationToken).ConfigureAwait(false);
                }
            }
            else
            {
                var binding = bindings.Endpoints.SingleOrDefault(binding => !binding.IsSubscription && binding.Route == route)
                    ?? throw new InvalidOperationException($"No local queue binding for '{route}'.");
                await services.ExecuteMessageAsync(binding.Operation, payload, headers, cancellationToken).ConfigureAwait(false);
            }
            activity?.SetStatus(ActivityStatusCode.Ok);
        }
        catch (Exception error)
        {
            activity?.SetStatus(ActivityStatusCode.Error, error.Message);
            throw;
        }
    }
}
