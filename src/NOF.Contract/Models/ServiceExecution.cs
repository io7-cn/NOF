using NOF.Abstraction;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace NOF.Contract;

/// <summary>Carries an in-process completion or rejection between generated service invocation and adapters.</summary>
/// <remarks>This runtime integration type is never a business response or a wire envelope.</remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public union ServiceExecution(ServiceCompleted, ServiceRejected)
{
    /// <summary>Returns the declared business value, or adapts/reports a policy rejection.</summary>
    /// <typeparam name="T">The operation's declared response type.</typeparam>
    /// <param name="createFailure">An optional caller-owned conversion for adapters handling rejection.</param>
    /// <param name="allowNull">Whether the operation explicitly declares a nullable response.</param>
    public T Require<T>(Func<Failure, T>? createFailure = null, bool allowNull = false)
    {
        if (Value is ServiceRejected rejected)
        {
            return createFailure is not null ? createFailure(rejected.Failure) : throw new ServiceRejectedException(rejected.Failure, rejected.Reason);
        }
        if (Value is not ServiceCompleted completed)
        {
            throw new InvalidOperationException("The service invocation did not produce an execution outcome.");
        }
        if (completed.Response is T typed)
        {
            if (typed is IUnion { Value: null })
            {
                throw new InvalidOperationException("The outcome response contains no case.");
            }
            return typed;
        }
        if (completed.Response is null && allowNull && default(T) is null)
        {
            return default!;
        }
        throw new InvalidOperationException($"The service response does not match '{typeof(T).FullName}'.");
    }
}
