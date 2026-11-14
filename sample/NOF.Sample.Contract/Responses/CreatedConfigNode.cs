namespace NOF.Sample;

public sealed record CreatedConfigNode(long Id);

/// <summary>Identifies the node whose children prevent deletion.</summary>
public sealed record NodeHasChildrenError(long NodeId, string Message = "Cannot delete node with children.");
