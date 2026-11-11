using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace NOF.Contract;

[EditorBrowsable(EditorBrowsableState.Never)]
public sealed class RequestOutboundContext : Context
{
    public IDictionary<string, string?> Headers { get; } = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Gets or sets the execution outcome carried through outbound middleware.</summary>
    public ServiceExecution Execution { get; set; }

    /// <summary>Gets or completes the business response. Null is a valid completed response.</summary>
    public object? Response
    {
        get => Execution.Value is ServiceCompleted completed ? completed.Response : null;
        set => Execution = new ServiceCompleted(value);
    }

    /// <summary>Rejects an outbound invocation before its business response is produced.</summary>
    public void Reject(NOF.Abstraction.Failure failure) => Execution = new ServiceRejected(failure);

    public required Type ServiceType { get; init; }

    public required MethodInfo MethodInfo { get; init; }

    public RequestOutboundContext()
    {
    }

    public RequestOutboundContext(Context context)
        : base(context?.Items ?? throw new ArgumentNullException(nameof(context)))
    {
    }

    [SetsRequiredMembers]
    private RequestOutboundContext(IReadOnlyDictionary<object, object?> items, RequestOutboundContext source)
        : base(items)
    {
        Headers = new Dictionary<string, string?>(source.Headers, StringComparer.OrdinalIgnoreCase);
        Execution = source.Execution;
        ServiceType = source.ServiceType;
        MethodInfo = source.MethodInfo;
    }

    protected override Context Clone(IReadOnlyDictionary<object, object?> items)
        => new RequestOutboundContext(items, this);
}
