using NOF.Abstraction;
using NOF.Contract;

namespace NOF.Application;

/// <summary>Executes a selected operation in the current host scope and waits for completion.</summary>
public interface IOperationExecutor
{
    /// <summary>Runs operation policies before constructing its implementation.</summary>
    Task<InvocationContext> ExecuteAsync(ServiceOperationDescriptor operation, object input,
        Context context, InvocationOrigin origin, CancellationToken cancellationToken = default);
}

/// <summary>Invokes an operation in an isolated scope with the caller's verified identity.</summary>
public interface IOperationInvoker
{
    /// <summary>Waits for operation completion; this API never completes merely on enqueue.</summary>
    Task<ServiceExecution> InvokeAsync(ServiceOperationDescriptor operation, object input,
        Context context, CancellationToken cancellationToken = default);
}
