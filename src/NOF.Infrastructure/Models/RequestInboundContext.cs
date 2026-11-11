using NOF.Abstraction;
using NOF.Contract;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace NOF.Infrastructure;

[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class RequestInboundContext : Context
{
    private readonly RequestInboundState _state;

    public object? Response => Execution.Value is ServiceCompleted completed ? completed.Response : null;

    /// <summary>Gets the execution outcome without conflating rejection with business failures.</summary>
    public ServiceExecution Execution => _state.Execution;

    /// <summary>Gets a policy rejection, if execution was rejected.</summary>
    public Failure? Rejection => Execution.Value is ServiceRejected rejected ? rejected.Failure : null;

    public IDictionary<string, string?> ResponseHeaders => _state.ResponseHeaders;

    /// <summary>Whether identity was copied from the caller's trusted local scope rather than an HTTP token.</summary>
    public bool IsLocalInvocation { get; set; }

    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicMethods)]
    public required Type ServiceType { get; init; }

    public required MethodInfo ServiceMethodInfo { get; init; }

    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicMethods | DynamicallyAccessedMemberTypes.NonPublicMethods)]
    public required Type HandlerType { get; init; }

    public required MethodInfo HandlerMethodInfo { get; init; }

    public required Type RequestType { get; init; }

    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicMethods)]
    public required Type ResponseType { get; init; }

    /// <summary>Completes execution with the operation's unchanged business value, including null.</summary>
    public void SetResponse(object? response) => _state.Execution = new ServiceCompleted(response);

    /// <summary>Rejects execution without manufacturing a business response.</summary>
    public void Reject(Failure failure) => _state.Execution = new ServiceRejected(failure);

    [SetsRequiredMembers]
    private RequestInboundContext(IReadOnlyDictionary<object, object?> items, RequestInboundContext source)
        : base(items)
    {
        _state = source._state;
        ServiceType = source.ServiceType;
        ServiceMethodInfo = source.ServiceMethodInfo;
        HandlerType = source.HandlerType;
        HandlerMethodInfo = source.HandlerMethodInfo;
        RequestType = source.RequestType;
        ResponseType = source.ResponseType;
        IsLocalInvocation = source.IsLocalInvocation;
    }

    public RequestInboundContext()
    {
        _state = new RequestInboundState();
    }

    protected override Context Clone(IReadOnlyDictionary<object, object?> items)
        => new RequestInboundContext(items, this);

    // Shared by immutable context clones so policies observe the same execution outcome.
    private sealed class RequestInboundState
    {
        public ServiceExecution Execution { get; set; }

        public IDictionary<string, string?> ResponseHeaders { get; } = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
    }
}
