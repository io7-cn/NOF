using System.Diagnostics.CodeAnalysis;

namespace NOF.Application;

/// <summary>A statically selectable generated application operation.</summary>
public interface IServiceOperationDefinition
{
    /// <summary>Gets the complete operation identity and business contract.</summary>
    static abstract ServiceOperationDescriptor Descriptor { get; }
    /// <summary>Dispatches its known request/response types to an adapter without runtime generic activation.</summary>
    static abstract TResult Describe<TResult>(IServiceOperationDescriptorVisitor<TResult> visitor);
}

/// <summary>A transport-independent bridge retaining operation type arguments for adapters.</summary>
public interface IServiceOperationDescriptorVisitor<out TResult>
{
    /// <summary>Visits a statically known request and business result type.</summary>
    TResult Visit<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicConstructors)] TRequest, TResponse>(ServiceOperationDescriptor<TRequest, TResponse> descriptor);
}
