namespace NOF.Abstraction;

/// <summary>Describes a permission group independently of its Attribute or object declaration form.</summary>
public interface IPermissionMetadata
{
    /// <summary>Gets the nonempty group of stable permission identifiers.</summary>
    IReadOnlyList<string> Permissions { get; }
    /// <summary>Gets how permissions in this group are combined.</summary>
    PermissionMatch Match { get; }
}
