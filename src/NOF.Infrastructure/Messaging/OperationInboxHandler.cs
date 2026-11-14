using NOF.Abstraction;
using NOF.Application;
using NOF.Contract;

namespace NOF.Infrastructure;

/// <summary>Invokes one selected operation from a transport-owned Inbox handler using the common execution boundary.</summary>
public sealed class OperationInboxHandler(ServiceOperationDescriptor operation, IObjectSerializer serializer, IOperationExecutor executor) : IInboxHandler
{
    /// <summary>Executes the selected operation in the Inbox transaction and scope.</summary>
    public async Task<object?> HandleAsync(ReadOnlyMemory<byte> payload,
        IEnumerable<KeyValuePair<string, string?>>? headers, CancellationToken cancellationToken)
    {
        var input = serializer.Deserialize(payload, operation.RequestType)
            ?? throw new InvalidOperationException($"Cannot deserialize message for '{operation.Id}'.");
        var context = Context.Empty.WithItems(InboundContextReflection.ReadHeaderItems(headers));
        var invocation = await executor.ExecuteAsync(operation, input, context, InvocationOrigin.Message, cancellationToken).ConfigureAwait(false);
        if (invocation is not IFailure && InvocationResult.Value(invocation) is not Empty)
        {
            throw new InvalidOperationException("The message operation did not return Empty or a declared filter failure.");
        }
        return invocation;
    }
}
