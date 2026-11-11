using NOF.Abstraction;

namespace NOF.Contract;

/// <summary>An invocation rejected by a policy before producing a business response.</summary>
public sealed record ServiceRejected
{
    /// <summary>Creates a rejection with shared failure details.</summary>
    public ServiceRejected(Failure failure)
    {
        ArgumentNullException.ThrowIfNull(failure);
        Failure = failure;
    }

    /// <summary>Gets the policy rejection details.</summary>
    public Failure Failure { get; }
}
