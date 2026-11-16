using System.Reflection;

namespace NOF.Abstraction;

/// <summary>Immutable, transport-independent authorization requirements shared by execution and presentation.</summary>
public sealed class AuthorizationPolicy
{
    /// <summary>Creates a policy; permission groups imply authentication and combine with AND.</summary>
    public AuthorizationPolicy(bool requiresAuthentication, IEnumerable<PermissionRequirement>? requirements = null,
        IEnumerable<MemberInfo>? sources = null)
    {
        Requirements = Array.AsReadOnly((requirements ?? []).ToArray());
        RequiresAuthentication = requiresAuthentication || Requirements.Count > 0;
        Sources = Array.AsReadOnly((sources ?? []).Distinct().ToArray());
    }
    /// <summary>Gets a policy with no authentication or permission requirement.</summary>
    public static AuthorizationPolicy Anonymous { get; } = new(false);
    /// <summary>Gets whether a caller must be authenticated.</summary>
    public bool RequiresAuthentication { get; }
    /// <summary>Gets the independent groups that must all be satisfied.</summary>
    public IReadOnlyList<PermissionRequirement> Requirements { get; }
    /// <summary>Gets the effective source members, including authentication-only or anonymous declarations.</summary>
    public IReadOnlyList<MemberInfo> Sources { get; }
    /// <summary>Combines independent branches; one branch cannot remove another branch's requirements.</summary>
    public static AuthorizationPolicy Combine(params AuthorizationPolicy[] policies)
        => new(policies.Any(policy => policy.RequiresAuthentication), policies.SelectMany(policy => policy.Requirements),
            policies.SelectMany(policy => policy.Sources));
}
