namespace NOF.Application;

/// <summary>A completed use case. Its response may itself be a business failure or a valid null.</summary>
/// <typeparam name="TResponse">The declared business response.</typeparam>
/// <param name="Response">The handler's unchanged response.</param>
public sealed record RequestCompleted<TResponse>(TResponse Response);
