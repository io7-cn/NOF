namespace NOF.Application;

/// <summary>Compile-time business result metadata; it has no filter or transport dependency.</summary>
public sealed record ServiceOperationResultCase(Type CaseType, bool IsFailure = false, string? Name = null);
