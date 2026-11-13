namespace NOF.Contract;

/// <summary>Provides the runtime invocation boundary for a generated application service operation.</summary>
public abstract class ServiceOperation
{
    /// <summary>Gets the operation's application input type.</summary>
    public abstract Type RequestType { get; }

    /// <summary>Gets the declared business response type.</summary>
    public abstract Type ResponseType { get; }

    /// <summary>Gets whether the contract explicitly permits a null business response.</summary>
    public virtual bool AllowsNullResponse => false;

    /// <summary>Invokes the operation without imposing a wire response interface.</summary>
    public abstract Task<object?> HandleAsync(object request, InvocationContext context, CancellationToken cancellationToken);
}

/// <summary>Implements one generated application service operation with its declared business response.</summary>
/// <typeparam name="TRequest">The application input.</typeparam>
/// <typeparam name="TResponse">The business response, including DTOs, nullable values and unions.</typeparam>
public abstract class ServiceOperation<TRequest, TResponse> : ServiceOperation
{
    /// <inheritdoc />
    public override Type RequestType => typeof(TRequest);

    /// <inheritdoc />
    public override Type ResponseType => typeof(TResponse);

    /// <inheritdoc />
    public sealed override async Task<object?> HandleAsync(object request, InvocationContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        var response = await HandleAsync((TRequest)request, context, cancellationToken).ConfigureAwait(false);
        if (response is null && !AllowsNullResponse)
        {
            throw new InvalidOperationException("The application operation returned null for a non-nullable response.");
        }
        return response;
    }

    /// <summary>Executes the operation with its original application input and response types.</summary>
    public abstract Task<TResponse> HandleAsync(TRequest request, InvocationContext context, CancellationToken cancellationToken);
}
