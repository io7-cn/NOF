using System.Diagnostics.CodeAnalysis;

namespace NOF.Application;

/// <summary>A transport-independent bridge retaining operation type arguments for adapters.</summary>
public interface IOperationDescriptorVisitor<out TResult>
{
    /// <summary>Visits a one-way operation without a business response type.</summary>
    TResult Visit<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicConstructors)] TRequest>(OperationDescriptor<TRequest> descriptor);
    /// <summary>Visits a statically known request and business result type.</summary>
    TResult Visit<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicConstructors)] TRequest, TResponse>(OperationDescriptor<TRequest, TResponse> descriptor);
}
