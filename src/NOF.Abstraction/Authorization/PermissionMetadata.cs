namespace NOF.Abstraction;

/// <summary>Combines the permissions inside one declaration; separate declarations always combine with AND.</summary>
public enum PermissionMatch
{
    /// <summary>Every listed permission must be granted.</summary>
    All,
    /// <summary>At least one listed permission must be granted.</summary>
    Any
}

/// <summary>Describes a permission group independently of its Attribute or object declaration form.</summary>
public interface IPermissionMetadata
{
    /// <summary>Gets the nonempty group of stable permission identifiers.</summary>
    IReadOnlyList<string> Permissions { get; }
    /// <summary>Gets how permissions in this group are combined.</summary>
    PermissionMatch Match { get; }
}

/// <summary>Describes an available permission; it does not grant it to any identity.</summary>
public sealed record PermissionDefinition
{
    /// <summary>Creates a stable permission with a display name and optional presentation group.</summary>
    public PermissionDefinition(string id, string name, string? group = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Id = id;
        Name = name;
        Group = group;
    }
    /// <summary>Gets the stable identifier, compared ordinally without case sensitivity.</summary>
    public string Id { get; }
    /// <summary>Gets the display name.</summary>
    public string Name { get; }
    /// <summary>Gets the optional display group.</summary>
    public string? Group { get; }
}
