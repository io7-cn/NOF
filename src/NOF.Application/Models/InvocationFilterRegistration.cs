namespace NOF.Application;

/// <summary>A host-readable extension definition. Registration alone does not execute the filter.</summary>
/// <param name="FilterType">The scoped extension type.</param>
/// <param name="Resolve">Resolves the extension in the invocation scope.</param>
/// <param name="Order">Default in-memory ordering; equal orders retain registration order.</param>
public sealed record InvocationFilterRegistration(Type FilterType, Func<IServiceProvider, IInvocationFilter> Resolve, int Order);
