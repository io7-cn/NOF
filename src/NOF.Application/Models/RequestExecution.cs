namespace NOF.Application;

/// <summary>Separates execution rejection from a completed handler's business response. This is not a wire envelope.</summary>
/// <typeparam name="TResponse">The declared business response.</typeparam>
public union RequestExecution<TResponse>(RequestCompleted<TResponse>, RequestRejected);
