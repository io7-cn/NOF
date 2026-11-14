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
