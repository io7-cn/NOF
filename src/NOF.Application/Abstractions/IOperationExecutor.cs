using NOF.Abstraction;
using NOF.Contract;

namespace NOF.Application;

/// <summary>Executes a reply-producing operation in an isolated scope with the caller's verified identity.</summary>
public interface IOperationExecutor
{
    /// <summary>Waits for completion; use transport-owned durable acceptance for one-way operations.</summary>
    Task<object?> ExecuteAsync(OperationDescriptor operation, object input,
        Context context, CancellationToken cancellationToken = default);
}
