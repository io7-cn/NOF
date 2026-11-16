using NOF.Abstraction;
using NOF.Application;
using NOF.Contract;
using System.Reflection;
using System.Security.Claims;

namespace NOF.Infrastructure;

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
