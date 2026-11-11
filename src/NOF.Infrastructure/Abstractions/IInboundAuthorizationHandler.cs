using NOF.Contract;
using System.Reflection;
using System.Security.Claims;

namespace NOF.Infrastructure;

public interface IInboundAuthorizationHandler
{
    ValueTask<InboundAuthorizationResult> AuthorizeAsync(
        InboundAuthorizationContext context,
        CancellationToken cancellationToken);
}

public enum InboundAuthorizationKind
{
    Request,
    Command,
    Notification
}

public sealed class InboundAuthorizationContext
{
    public required InboundAuthorizationKind Kind { get; init; }

    public required ClaimsPrincipal User { get; init; }

    public required Context ExecutionContext { get; init; }

    public required object Input { get; init; }

    public required Type HandlerType { get; init; }

    public required MethodInfo HandlerMethodInfo { get; init; }

    public Type? ServiceType { get; init; }

    public MethodInfo? ServiceMethodInfo { get; init; }

    public Type? MessageType { get; init; }
}

/// <summary>Contains either an explicit authorization grant or a denial.</summary>
public union InboundAuthorizationResult(InboundAuthorizationResult.Allowed, InboundAuthorizationResult.Denied)
{
    /// <summary>Grants permission to execute the operation.</summary>
    public sealed record Allowed;

    /// <summary>Rejects the operation with a business failure and an HTTP challenge.</summary>
    public sealed record Denied(
        Failure Result,
        int StatusCode,
        IReadOnlyList<string> ChallengePermissions);

    /// <summary>Gets the shared authorization grant.</summary>
    public static Allowed Success { get; } = new();
}
