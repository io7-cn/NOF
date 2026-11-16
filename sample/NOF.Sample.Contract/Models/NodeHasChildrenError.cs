namespace NOF.Sample;

/// <summary>Identifies the node whose children prevent deletion.</summary>
public sealed record NodeHasChildrenError(long NodeId, string Message = "Cannot delete node with children.");
