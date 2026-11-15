using Microsoft.Extensions.DependencyInjection;
using NOF.Abstraction;
using NOF.Application;
using NOF.Contract;
using NOF.Hosting;
using System.Diagnostics;
using System.Text.Json.Nodes;

namespace NOF.Infrastructure;

/// <summary>Adapts message payloads to the operation boundary without receipt storage or deduplication.</summary>
public static class MessageEndpointExtensions
{
    /// <summary>
    /// Executes one delivery in an isolated scope with its semantic context. The caller owns
    /// acknowledgement and redelivery; application code owns persistence and idempotency.
    /// </summary>
    public static async Task ExecuteMessageAsync(this IServiceProvider services, OperationDescriptor operation,
        ReadOnlyMemory<byte> payload, IReadOnlyDictionary<string, string?>? headers,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(operation);
        if (!OperationInspector.CanSubmit(operation))
        {
            throw new ArgumentException("Message endpoints require a void operation without business failure cases.", nameof(operation));
        }
        cancellationToken.ThrowIfCancellationRequested();
        var metadata = headers is null ? new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, string?>(headers, StringComparer.OrdinalIgnoreCase);
        metadata.TryGetValue(NOFAbstractionConstants.Transport.Headers.Context, out var snapshot);
        var context = Context.Empty.WithItems(InboundContextReflection.ReadHeaderItems(metadata));
        if (snapshot is not null)
        {
            context = context.WithItems(JsonNode.Parse(snapshot) as JsonObject
                ?? throw new InvalidOperationException("The message context must be a JSON object."));
        }
        metadata.TryGetValue(NOFAbstractionConstants.Transport.Headers.TenantId, out var tenantId);
        context = context.WithTenantId(tenantId);
        context = metadata.TryGetValue(NOFAbstractionConstants.Transport.Headers.MessageId, out var messageId)
            ? context.WithItem(NOFAbstractionConstants.Transport.Headers.MessageId, messageId)
            : context.WithoutItem(NOFAbstractionConstants.Transport.Headers.MessageId);
        metadata.TryGetValue(NOFAbstractionConstants.Transport.Headers.TraceParent, out var traceParent);
        using var activity = NOFInfrastructureConstants.InboundPipeline.Source.StartActivityWithParent(
            $"Message: {operation.Id}", ActivityKind.Consumer, traceParent);
        await using var scope = services.CreateAsyncScope();
        using var binding = scope.ServiceProvider.BindNOF(context);
        var input = scope.ServiceProvider.GetRequiredService<IObjectSerializer>().Deserialize(payload, operation.RequestType)
            ?? throw new InvalidOperationException($"Cannot deserialize message for '{operation.Id}'.");
        var result = await scope.ServiceProvider.GetRequiredService<OperationExecutor>()
            .ExecuteInScopeAsync(operation, input, context, InvocationOrigin.Message, cancellationToken).ConfigureAwait(false);
        var value = InvocationResult.Value(result);
        if (value is IFailure failure)
        {
            throw new InvalidOperationException($"Message operation '{operation.Id}' was rejected: {failure}");
        }
        if (value is not null)
        {
            throw new InvalidOperationException("A one-way message operation cannot produce a business response.");
        }
    }
}
