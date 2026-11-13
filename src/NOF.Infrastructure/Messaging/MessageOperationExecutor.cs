using NOF.Application;
using NOF.Contract;

namespace NOF.Infrastructure;

/// <summary>Adapts a durable consumer binding to the same execution boundary used by HTTP and local calls.</summary>
public sealed class MessageOperationExecutor(MessageBindingRegistry bindings, IObjectSerializer serializer, IOperationExecutor executor)
{
    /// <summary>Executes one subscription in its existing inbox transaction and scope.</summary>
    public async Task ExecuteAsync(ReadOnlyMemory<byte> payload, string consumerId,
        IEnumerable<KeyValuePair<string, string?>>? headers, CancellationToken cancellationToken)
    {
        var binding = bindings.GetConsumer(consumerId);
        var input = serializer.Deserialize(payload, binding.MessageType)
            ?? throw new InvalidOperationException($"Cannot deserialize message for '{consumerId}'.");
        var context = Context.Empty.WithItems(InboundContextReflection.ReadHeaderItems(headers));
        var invocation = await executor.ExecuteAsync(binding.Operation, input, context, InvocationOrigin.Message, cancellationToken).ConfigureAwait(false);
        invocation.Execution.Require<Empty>();
    }
}
