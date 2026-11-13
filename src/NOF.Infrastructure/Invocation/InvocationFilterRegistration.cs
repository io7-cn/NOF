namespace NOF.Infrastructure;

/// <summary>The scope of one application rule.</summary>
public enum InvocationFilterScope
{
    /// <summary>All operations.</summary>
    Global,
    /// <summary>One service contract.</summary>
    Service,
    /// <summary>One stable operation identity.</summary>
    UseCase
}

/// <summary>An effective filter, its static metadata and scope provenance.</summary>
public sealed record InvocationFilterRegistration(Type FilterType, Func<IServiceProvider, IInvocationFilter> Resolve,
    Action<InvocationDescriptionContext> Describe, IReadOnlyList<InvocationFailureCase> FailureCases,
    InvocationFilterScope Scope, int Order, int Sequence);
