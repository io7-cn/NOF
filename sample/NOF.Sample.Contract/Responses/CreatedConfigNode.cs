namespace NOF.Sample;

public sealed record CreatedConfigNode(long Id);

public static class ConfigNodeErrorCodes
{
    public const string NotFound = "config_node.not_found";
    public const string ParentNotFound = "config_node.parent_not_found";
    public const string NameConflict = "config_node.name_conflict";
    public const string ParentCycle = "config_node.parent_cycle";
    public const string HasChildren = "config_node.has_children";
}
