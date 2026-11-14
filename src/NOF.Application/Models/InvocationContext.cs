using NOF.Abstraction;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace NOF.Application;

/// <summary>Identifies an execution boundary without assigning a message programming model.</summary>
public enum InvocationOrigin
{
    /// <summary>A trusted in-process caller.</summary>
    Local,
    /// <summary>An HTTP request.</summary>
    Http,
    /// <summary>A message consumer whose identity is established by its host.</summary>
    Message
}

/// <summary>Describes one operation execution, composing its immutable semantic context.</summary>
public sealed class InvocationContext
{
    /// <summary>Gets the semantic information allowed to flow into nested calls.</summary>
    public Context Context { get; private init; } = Context.Empty;
    /// <summary>Gets the operation selected by the host.</summary>
    public required ServiceOperationDescriptor Operation { get; init; }
    /// <summary>Gets a unique execution identifier, distinct from a transported message identifier.</summary>
    public string InvocationId { get; init; }
    /// <summary>Gets the execution entry boundary.</summary>
    public InvocationOrigin Origin { get; init; }
    /// <summary>Gets the implementation type without constructing it before policies run.</summary>
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicMethods | DynamicallyAccessedMemberTypes.NonPublicMethods)]
    public required Type HandlerType { get; init; }
    /// <summary>Gets implementation policy metadata.</summary>
    public required MethodInfo HandlerMethodInfo { get; init; }
    /// <summary>Gets the input type.</summary>
    public Type InputType => Operation.RequestType;
    /// <summary>Gets the service contract.</summary>
    public Type ServiceType => Operation.ServiceType;
    /// <summary>Gets the contract method.</summary>
    public MethodInfo ServiceMethodInfo => Operation.MethodInfo;
    /// <summary>Gets a diagnostic operation name.</summary>
    public string OperationName => Operation.Id;
    /// <summary>Gets the semantic tenant.</summary>
    public string TenantId => Context.TenantId;
    /// <summary>Gets semantic context items.</summary>
    public IReadOnlyDictionary<object, object?> Items => Context.Items;
    /// <summary>Reads a semantic context item.</summary>
    public bool TryGetItem(object key, out object? value) => Context.TryGetItem(key, out value);
    /// <summary>Creates an execution view carrying different semantic items and the same invocation identity.</summary>
    public InvocationContext WithContext(Context context) => new(this, context);
    /// <summary>Creates a view with one semantic item.</summary>
    public InvocationContext WithItem(object key, object? value) => WithContext(Context.WithItem(key, value));
    /// <summary>Creates a view with additional semantic items.</summary>
    public InvocationContext WithItems(IReadOnlyDictionary<object, object?> items) => WithContext(Context.WithItems(items));
    /// <summary>Creates a view with an item removed.</summary>
    public InvocationContext WithoutItem(object key) => WithContext(Context.WithoutItem(key));
    /// <summary>Creates a view with a normalized tenant.</summary>
    public InvocationContext WithTenantId(string? tenantId) => WithContext(Context.WithTenantId(tenantId));
    /// <summary>Creates an execution context.</summary>
    public InvocationContext()
    {
        InvocationId = Guid.NewGuid().ToString("N");
    }

    [SetsRequiredMembers]
    private InvocationContext(InvocationContext source, Context context)
    {
        ArgumentNullException.ThrowIfNull(context);
        Context = context;
        Operation = source.Operation;
        Origin = source.Origin;
        InvocationId = source.InvocationId;
        HandlerType = source.HandlerType;
        HandlerMethodInfo = source.HandlerMethodInfo;
    }
}
