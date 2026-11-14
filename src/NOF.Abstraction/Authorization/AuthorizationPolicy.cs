using System.Reflection;

namespace NOF.Abstraction;

/// <summary>A defensive snapshot of one permission group and its declaration source.</summary>
public sealed class PermissionRequirement : IPermissionMetadata
{
    /// <summary>Creates a validated requirement without retaining a mutable Attribute or input collection.</summary>
    public PermissionRequirement(IEnumerable<string> permissions, PermissionMatch match = PermissionMatch.All, MemberInfo? source = null)
    {
        ArgumentNullException.ThrowIfNull(permissions);
        var values = permissions.ToArray();
        if (values.Length == 0 || values.Any(string.IsNullOrWhiteSpace) || !Enum.IsDefined(match))
        {
            throw new ArgumentException("A permission requirement needs nonblank identifiers and a valid match mode.", nameof(permissions));
        }
        Permissions = Array.AsReadOnly(values.Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
        Match = match;
        Source = source;
    }
    /// <inheritdoc />
    public IReadOnlyList<string> Permissions { get; }
    /// <inheritdoc />
    public PermissionMatch Match { get; }
    /// <summary>Gets the source member when this requirement came from a declaration.</summary>
    public MemberInfo? Source { get; }
}

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
