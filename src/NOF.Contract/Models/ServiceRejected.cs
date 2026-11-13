using NOF.Abstraction;

namespace NOF.Contract;

/// <summary>Explains a policy rejection independently of transport response codes.</summary>
public enum RejectionReason
{
    /// <summary>An application execution policy declined the input.</summary>
    Policy,
    /// <summary>A verified identity is required.</summary>
    Authentication,
    /// <summary>The verified identity lacks permission.</summary>
    Authorization
}

/// <summary>An invocation rejected by a policy before producing a business response.</summary>
public sealed record ServiceRejected
{
    /// <summary>Creates a rejection with shared failure details.</summary>
    public ServiceRejected(Failure failure, RejectionReason reason = RejectionReason.Policy, IReadOnlyList<string>? requiredPermissions = null)
    {
        ArgumentNullException.ThrowIfNull(failure);
        Failure = failure;
        Reason = reason;
        RequiredPermissions = requiredPermissions?.ToArray() ?? [];
    }

    /// <summary>Gets the policy rejection details.</summary>
    public Failure Failure { get; }
    /// <summary>Gets the transport-independent rejection category.</summary>
    public RejectionReason Reason { get; }
    /// <summary>Gets permissions required by the rejecting policy.</summary>
    public IReadOnlyList<string> RequiredPermissions { get; }
}
