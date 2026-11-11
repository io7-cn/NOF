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

    public IUnion? Response => _state.Response;

    public IDictionary<string, string?> ResponseHeaders => _state.ResponseHeaders;

    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicMethods)]
    public required Type ServiceType { get; init; }

    public required MethodInfo ServiceMethodInfo { get; init; }

    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicMethods | DynamicallyAccessedMemberTypes.NonPublicMethods)]
    public required Type HandlerType { get; init; }

    public required MethodInfo HandlerMethodInfo { get; init; }

    public required Type RequestType { get; init; }

    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicMethods)]
    public required Type ResponseType { get; init; }

    public void SetResponse(IUnion response)
    {
        ArgumentNullException.ThrowIfNull(response);
        if (response.Value is null)
        {
            throw new InvalidOperationException("The RPC response contains no outcome case.");
        }
        _state.Response = response;
    }

    public void SetResponse(Failure failure)
    {
        ArgumentNullException.ThrowIfNull(failure);

        _state.Response = new Result(failure);
    }

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
    }

    public RequestInboundContext()
    {
        _state = new RequestInboundState();
    }

    protected override Context Clone(IReadOnlyDictionary<object, object?> items)
        => new RequestInboundContext(items, this);

    private sealed class RequestInboundState
    {
        public IUnion? Response { get; set; }

        public IDictionary<string, string?> ResponseHeaders { get; } = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
    }
}
