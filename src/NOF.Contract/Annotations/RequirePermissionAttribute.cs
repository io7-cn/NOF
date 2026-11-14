namespace NOF.Contract;

/// <summary>
/// Permission requirement attribute for marking handlers or messages that need specific permissions
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Interface, AllowMultiple = true, Inherited = false)]
public sealed class RequirePermissionAttribute : Attribute, IPermissionMetadata
{
    /// <summary>Creates a nonempty permission group; multiple groups at one declaration are combined with AND.</summary>
    public RequirePermissionAttribute(params string[] permissions)
    {
        ArgumentNullException.ThrowIfNull(permissions);
        if (permissions.Length == 0 || permissions.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException("Declare at least one nonblank permission. Use RequireAuthenticated for login-only access.", nameof(permissions));
        }
        Permissions = Array.AsReadOnly(permissions.ToArray());
    }

    /// <inheritdoc />
    public IReadOnlyList<string> Permissions { get; }
    /// <inheritdoc />
    public new PermissionMatch Match { get; set; } = PermissionMatch.All;
}
