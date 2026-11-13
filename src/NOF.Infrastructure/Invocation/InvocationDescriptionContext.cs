using NOF.Application;
using System.Reflection;

namespace NOF.Infrastructure;

/// <summary>Immutable operation/configuration metadata and selection within one filter's catalog.</summary>
public sealed class InvocationDescriptionContext
{
    private readonly IReadOnlyList<InvocationFailureCase> _catalog;
    private readonly IReadOnlyList<object> _metadata;
    private readonly List<InvocationFailureCase> _included = [];
    /// <summary>Creates a description scope without any scoped service access.</summary>
    public InvocationDescriptionContext(InvocationContext invocation, IReadOnlyList<InvocationFailureCase> catalog,
        IReadOnlyList<object>? metadata = null)
    {
        _invocation = invocation;
        _catalog = catalog;
        _metadata = metadata ?? [];
    }
    private readonly InvocationContext _invocation;
    /// <summary>Gets the selected operation.</summary>
    public ServiceOperationDescriptor Operation => _invocation.Operation;
    /// <summary>Gets the entry origin.</summary>
    public InvocationOrigin Origin => _invocation.Origin;
    /// <summary>Gets selected cases as a defensive snapshot.</summary>
    public IReadOnlyList<InvocationFailureCase> Included => Array.AsReadOnly(_included.ToArray());
    /// <summary>Reads configuration or the most specific implementation/contract attribute.</summary>
    public TMetadata? GetMetadata<TMetadata>() where TMetadata : class
        => _metadata.OfType<TMetadata>().LastOrDefault()
            ?? new MemberInfo[] { _invocation.HandlerMethodInfo, _invocation.HandlerType, Operation.MethodInfo, Operation.ServiceType }
                .SelectMany(member => member.GetCustomAttributes(inherit: true).OfType<TMetadata>()).FirstOrDefault();
    /// <summary>Includes a generated branch owned by this filter; undeclared types are rejected.</summary>
    public void Include<TFailure>()
    {
        var entry = _catalog.SingleOrDefault(item => item.CaseType == typeof(TFailure))
            ?? throw new InvalidOperationException($"Failure '{typeof(TFailure)}' is not declared by the describing filter.");
        if (!_included.Contains(entry))
        {
            _included.Add(entry);
        }
    }
    /// <summary>Includes every declared failure without constructing the filter.</summary>
    public void IncludeAll()
    {
        foreach (var entry in _catalog)
        {
            if (!_included.Contains(entry))
            {
                _included.Add(entry);
            }
        }
    }
}
