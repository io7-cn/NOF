namespace NOF.Infrastructure;

/// <summary>A generated failure declaration and typed payload reader; contains no transport policy.</summary>
public sealed record InvocationFailureCase(string Id, Type CaseType, Type ErrorType, string? Title,
    Func<object, object?> ReadError, Func<object, string?> ReadDetail);
