namespace NOF.Contract;

/// <summary>
/// Marks an API as allowing anonymous access.
/// Cancels inherited authorization defaults in its own declaration branch only.
/// </summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Interface, Inherited = false)]
public sealed class AllowAnonymousAttribute : Attribute;

