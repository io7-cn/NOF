namespace NOF.Infrastructure;

/// <summary>An effective filter, its static metadata and scope provenance.</summary>
public sealed record InvocationFilterRegistration(Type FilterType, Func<IServiceProvider, IInvocationFilter> Resolve,
    Action<InvocationDescriptionContext> Describe, IReadOnlyList<InvocationFailureCase> FailureCases,
    InvocationFilterScope Scope, int Order, int Sequence);
