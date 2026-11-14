using NOF.Abstraction;
using NOF.Application;
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

public sealed class InboundAuthorizationContext
{
    public required ClaimsPrincipal User { get; init; }

    public required Context ExecutionContext { get; init; }

    public required object Input { get; init; }

    public required Type HandlerType { get; init; }

    public required MethodInfo HandlerMethodInfo { get; init; }

    public Type? ServiceType { get; init; }

    public MethodInfo? ServiceMethodInfo { get; init; }

    public Type? InputType { get; init; }
}

/// <summary>Classifies the authorization provider's denial before the filter returns its typed case.</summary>
public enum AuthorizationFailureReason
{
    /// <summary>Other provider policy.</summary>
    Policy,
    /// <summary>Authentication is required.</summary>
    Authentication,
    /// <summary>Permission or tenant trust is insufficient.</summary>
    Authorization
}

/// <summary>Contains either an explicit authorization grant or a denial.</summary>
public union InboundAuthorizationResult(InboundAuthorizationResult.Allowed, InboundAuthorizationResult.Denied)
{
    /// <summary>Grants permission to execute the operation.</summary>
    public sealed record Allowed;

    /// <summary>Rejects the operation with a failure and the semantic authorization denial.</summary>
    public sealed record Denied(
        Failure Result,
        AuthorizationFailureReason Reason,
        IReadOnlyList<string> ChallengePermissions);

    /// <summary>Gets the shared authorization grant.</summary>
    public static Allowed Success { get; } = new();
}
