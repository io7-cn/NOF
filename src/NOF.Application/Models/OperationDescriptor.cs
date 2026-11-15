using NOF.Contract;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;

namespace NOF.Application;

/// <summary>Identifies one contract operation independently of its implementation and transport.</summary>
public abstract class OperationDescriptor
{
    /// <summary>Creates execution metadata with a complete Application-owned output type.</summary>
    protected OperationDescriptor(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicMethods | DynamicallyAccessedMemberTypes.Interfaces)] Type serviceType,
        string operationName, bool allowsNullResponse, Type? directInputType = null)
    {
        ArgumentNullException.ThrowIfNull(serviceType);
        ArgumentException.ThrowIfNullOrWhiteSpace(operationName);
        DeclaringType = serviceType;
        OperationName = operationName;
        AllowsNullResponse = allowsNullResponse;
        MethodInfo = (directInputType is null ? serviceType.GetMethod(operationName)
            : serviceType.GetMethod(operationName, [directInputType, typeof(InvocationContext), typeof(CancellationToken)]))
            ?? throw new ArgumentException($"Operation '{Id}' does not exist.", nameof(operationName));
    }
    /// <summary>Dispatches the preserved request/response types without runtime generic activation.</summary>
    public abstract TResult Accept<TResult>(IOperationDescriptorVisitor<TResult> visitor);
    /// <summary>Gets the declaring Contract or standalone handler.</summary>
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicMethods | DynamicallyAccessedMemberTypes.Interfaces)]
    public Type DeclaringType { get; }
    /// <summary>Gets the optional public Contract; standalone handlers have no Contract policy.</summary>
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicMethods | DynamicallyAccessedMemberTypes.Interfaces)]
    public Type? ContractType => DeclaringType.IsInterface ? DeclaringType : null;
    /// <summary>Gets the declared operation name.</summary>
    public string OperationName { get; }
    /// <summary>Gets the contract-qualified identity.</summary>
    public string Id => ContractType is null ? DeclaringType.FullName ?? DeclaringType.Name : $"{DeclaringType.FullName}/{OperationName}";
    /// <summary>Gets the business input type.</summary>
    public abstract Type RequestType { get; }
    /// <summary>Gets resolved Contract method and policy metadata.</summary>
    public MethodInfo MethodInfo { get; }
    /// <summary>Gets the complete business output type, including generated failure branches.</summary>
    public abstract Type ResponseType { get; }
    /// <summary>Gets whether execution allows a null output.</summary>
    public bool AllowsNullResponse { get; }
    /// <summary>Gets statically described business branches.</summary>
    public virtual IReadOnlyList<OperationResultCase> ResultCases => IsOneWay ? [] : [new(ResponseType)];
    /// <summary>Gets whether the operation has no business reply channel.</summary>
    public bool IsOneWay => ResponseType == typeof(void);
}

/// <summary>A typed execution descriptor whose output is known by the Application implementation.</summary>
public sealed class OperationDescriptor<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicConstructors)] TRequest, TResponse> : OperationDescriptor
{
    /// <summary>Creates metadata for a standalone reply-producing handler.</summary>
    public OperationDescriptor(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicMethods | DynamicallyAccessedMemberTypes.Interfaces)] Type handlerType,
        bool allowsNullResponse = false, IReadOnlyList<OperationResultCase>? resultCases = null)
        : base(handlerType, nameof(OperationHandler.HandleAsync), allowsNullResponse, typeof(TRequest))
    {
        if (!typeof(OperationHandler<TRequest, TResponse>).IsAssignableFrom(handlerType))
        {
            throw new ArgumentException("The handler must match the descriptor's input and response types.", nameof(handlerType));
        }
        ResultCases = Array.AsReadOnly((resultCases ?? [new(typeof(TResponse))]).ToArray());
    }
    /// <summary>Creates a typed execution descriptor and validates the declared input.</summary>
    public OperationDescriptor(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicMethods | DynamicallyAccessedMemberTypes.Interfaces)] Type serviceType,
        string operationName, bool allowsNullResponse = false, IReadOnlyList<OperationResultCase>? resultCases = null) : base(serviceType, operationName, allowsNullResponse)
    {
        if (MethodInfo.ReturnType == typeof(void) || MethodInfo.GetParameters() is not [{ ParameterType: var input }] || input != typeof(TRequest))
        {
            throw new ArgumentException("The operation input must match its contract.", nameof(serviceType));
        }
        ResultCases = Array.AsReadOnly((resultCases ?? [new(typeof(TResponse))]).ToArray());
    }
    /// <inheritdoc />
    public override TResult Accept<TResult>(IOperationDescriptorVisitor<TResult> visitor) => visitor.Visit(this);
    /// <inheritdoc />
    public override Type RequestType => typeof(TRequest);
    /// <inheritdoc />
    public override Type ResponseType => typeof(TResponse);
    /// <inheritdoc />
    public override IReadOnlyList<OperationResultCase> ResultCases { get; }
}

/// <summary>A statically typed descriptor for a void Contract operation with no business response.</summary>
public sealed class OperationDescriptor<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicConstructors)] TRequest> : OperationDescriptor
{
    /// <summary>Creates metadata for a standalone one-way handler.</summary>
    public OperationDescriptor(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicMethods | DynamicallyAccessedMemberTypes.Interfaces)] Type handlerType)
        : base(handlerType, nameof(OperationHandler.HandleAsync), false, typeof(TRequest))
    {
        if (!typeof(OperationHandler<TRequest>).IsAssignableFrom(handlerType))
        {
            throw new ArgumentException("The handler must match the descriptor's one-way input.", nameof(handlerType));
        }
    }
    /// <summary>Creates and validates a one-way operation; Empty/value contracts cannot be bound as messages.</summary>
    public OperationDescriptor(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicMethods | DynamicallyAccessedMemberTypes.Interfaces)] Type serviceType,
        string operationName) : base(serviceType, operationName, false)
    {
        if (MethodInfo.ReturnType != typeof(void) || MethodInfo.IsDefined(typeof(FailureCaseAttribute), inherit: false)
            || MethodInfo.GetParameters() is not [{ ParameterType: var input }] || input != typeof(TRequest))
        {
            throw new ArgumentException("A one-way descriptor requires a void Contract method with matching input and no FailureCase.", nameof(serviceType));
        }
    }
    /// <inheritdoc />
    public override TResult Accept<TResult>(IOperationDescriptorVisitor<TResult> visitor) => visitor.Visit(this);
    /// <inheritdoc />
    public override Type RequestType => typeof(TRequest);
    /// <inheritdoc />
    public override Type ResponseType => typeof(void);
}
