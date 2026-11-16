namespace NOF.Abstraction;

/// <summary>Combines the permissions inside one declaration; separate declarations always combine with AND.</summary>
public enum PermissionMatch
{
    /// <summary>Every listed permission must be granted.</summary>
    All,
    /// <summary>At least one listed permission must be granted.</summary>
    Any
}
