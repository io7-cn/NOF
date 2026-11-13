using NOF.Abstraction;
using NOF.Application;
using NOF.Contract;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace NOF.Infrastructure;

/// <summary>Application-service adapter metadata, including optional HTTP response metadata.</summary>
public sealed class RequestInboundContext : InvocationContext
{
    private readonly IDictionary<string, string?> _responseHeaders;

    /// <inheritdoc />
    public override InvocationKind Kind => InvocationKind.ApplicationService;

    /// <inheritdoc />
    public override string OperationName => $"{ServiceType.DisplayName}.{ServiceMethodInfo.Name}";

    /// <summary>Gets metadata to be interpreted by the invoking adapter.</summary>
    public IDictionary<string, string?> ResponseHeaders => _responseHeaders;

    /// <summary>Whether trusted identity was copied from a local caller.</summary>
    public bool IsLocalInvocation { get; set; }

    /// <summary>Gets the service contract.</summary>
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicMethods)]
    public required Type ServiceType { get; init; }

    /// <summary>Gets the contract method.</summary>
    public required MethodInfo ServiceMethodInfo { get; init; }

    /// <summary>Gets the declared business response type.</summary>
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicMethods)]
    public required Type ResponseType { get; init; }

    /// <summary>Creates an application invocation adapter context.</summary>
    public RequestInboundContext() => _responseHeaders = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

    [SetsRequiredMembers]
    private RequestInboundContext(IReadOnlyDictionary<object, object?> items, RequestInboundContext source) : base(items, source)
    {
        _responseHeaders = source._responseHeaders;
        ServiceType = source.ServiceType;
        ServiceMethodInfo = source.ServiceMethodInfo;
        ResponseType = source.ResponseType;
        IsLocalInvocation = source.IsLocalInvocation;
    }

    /// <inheritdoc />
    protected override Context Clone(IReadOnlyDictionary<object, object?> items) => new RequestInboundContext(items, this);
}
