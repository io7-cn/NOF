using System.Security.Claims;

namespace NOF.Abstraction;

/// <summary>A semantic authorization decision, without HTTP codes or presentation fields.</summary>
public union PermissionCheckResult(PermissionCheckResult.Granted, PermissionCheckResult.AuthenticationRequired, PermissionCheckResult.Denied)
{
    /// <summary>The policy is satisfied.</summary>
    public sealed record Granted;
    /// <summary>The policy requires an authenticated identity.</summary>
    public sealed record AuthenticationRequired;
    /// <summary>An authenticated identity does not satisfy a permission group.</summary>
    public sealed record Denied(IReadOnlyList<string> RequiredPermissions);
}
