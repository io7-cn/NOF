using NOF.Contract;

namespace NOF.Authorization;

/// <summary>Host-owned permission definitions, frozen on first use; definitions never imply grants.</summary>
public sealed class PermissionCatalog
{
    private readonly Dictionary<string, PermissionDefinition> _definitions = new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyList<PermissionDefinition>? _snapshot;

    /// <summary>Adds one definition before first use; duplicate stable identifiers are configuration errors.</summary>
    public void Add(PermissionDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (_snapshot is not null)
        {
            throw new InvalidOperationException("The permission catalog is frozen.");
        }
        if (!_definitions.TryAdd(definition.Id, definition))
        {
            throw new InvalidOperationException($"Permission '{definition.Id}' is already defined.");
        }
    }
    /// <summary>Lists definitions in stable identifier order as an immutable snapshot.</summary>
    public IReadOnlyList<PermissionDefinition> GetAll()
        => _snapshot ??= Array.AsReadOnly(_definitions.Values.OrderBy(value => value.Id, StringComparer.OrdinalIgnoreCase).ToArray());
    /// <summary>Resolves a declared identifier; unknown permissions are configuration errors, never implicit grants.</summary>
    public PermissionDefinition Get(string id)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        _ = GetAll();
        return _definitions.GetValueOrDefault(id)
            ?? throw new InvalidOperationException($"Permission '{id}' is not defined. Register its PermissionDefinition with AddPermissions.");
    }
    /// <summary>Checks every permission reference, including alternatives of Any groups, without querying user grants.</summary>
    public void Validate(AuthorizationPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        foreach (var permission in policy.Requirements.SelectMany(requirement => requirement.Permissions))
        {
            _ = Get(permission);
        }
    }
}
