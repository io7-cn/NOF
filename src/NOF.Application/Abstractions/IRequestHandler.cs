using NOF.Contract;

namespace NOF.Application;

/// <summary>Handles an application request without prescribing its transport or response shape.</summary>
/// <typeparam name="TRequest">The application input.</typeparam>
/// <typeparam name="TResponse">The business response, including nullable values or business unions.</typeparam>
public interface IRequestHandler<in TRequest, TResponse>
{
    /// <summary>Executes the business use case.</summary>
    Task<TResponse> HandleAsync(TRequest request, Context context, CancellationToken cancellationToken);
}
