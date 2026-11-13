using NOF.Abstraction;
using NOF.Contract;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace NOF.Application;

/// <summary>Identifies the logical application entry point independently of its transport.</summary>
public enum InvocationKind
{
    /// <summary>A declared application service operation.</summary>
    ApplicationService,
    /// <summary>A command delivery.</summary>
    Command,
    /// <summary>A notification delivery.</summary>
    Notification
}

/// <summary>Common execution metadata and outcome visible to host-selected invocation extensions.</summary>
public abstract class InvocationContext : Context
{
    private readonly StrongBox<ServiceExecution> _execution;

    /// <summary>Gets the logical invocation kind.</summary>
    public abstract InvocationKind Kind { get; }

    /// <summary>Gets the implementation type, resolved only after execution policies run.</summary>
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicMethods | DynamicallyAccessedMemberTypes.NonPublicMethods)]
    public required Type HandlerType { get; init; }

    /// <summary>Gets the implementation method.</summary>
    public required MethodInfo HandlerMethodInfo { get; init; }

    /// <summary>Gets the application input type.</summary>
    public required Type InputType { get; init; }

    /// <summary>Gets the logical operation name used for diagnostics.</summary>
    public virtual string OperationName => InputType.DisplayName;

    /// <summary>Gets the execution outcome shared by context clones.</summary>
    public ServiceExecution Execution => _execution.Value;

    /// <summary>Gets the completed business value, when available.</summary>
    public object? Response => Execution.Value is ServiceCompleted completed ? completed.Response : null;

    /// <summary>Gets the policy rejection, when available.</summary>
    public Failure? Rejection => Execution.Value is ServiceRejected rejected ? rejected.Failure : null;

    /// <summary>Completes execution with the business value, including a declared null.</summary>
    public void SetResponse(object? response) => _execution.Value = new ServiceCompleted(response);

    /// <summary>Rejects execution independently of business response types.</summary>
    public void Reject(Failure failure) => _execution.Value = new ServiceRejected(failure);

    /// <summary>Creates an invocation context with no outcome.</summary>
    protected InvocationContext() => _execution = new();

    /// <summary>Copies immutable items and metadata while sharing the invocation outcome.</summary>
    [SetsRequiredMembers]
    protected InvocationContext(IReadOnlyDictionary<object, object?> items, InvocationContext source) : base(items)
    {
        _execution = source._execution;
        HandlerType = source.HandlerType;
        HandlerMethodInfo = source.HandlerMethodInfo;
        InputType = source.InputType;
    }
}
