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
