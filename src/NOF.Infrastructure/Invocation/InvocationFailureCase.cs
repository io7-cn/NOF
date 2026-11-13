namespace NOF.Infrastructure;

/// <summary>Marks a generated filter failure value without wrapping it or imposing a transport shape.</summary>
public interface IInvocationFailure;

/// <summary>A generated failure declaration and typed payload reader; contains no transport policy.</summary>
public sealed record InvocationFailureCase(string Id, Type CaseType, Type ErrorType, string? ErrorCode,
    Func<object, object?> ReadError, Func<object, string?> ReadMessage);
