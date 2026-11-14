namespace NOF.Contract;

/// <summary>Requires an authenticated caller without requiring a specific permission.</summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Interface, Inherited = false)]
public sealed class RequireAuthenticatedAttribute : Attribute;
