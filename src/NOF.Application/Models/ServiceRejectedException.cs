using NOF.Abstraction;

namespace NOF.Application;

/// <summary>Reports a rejected service invocation when its business response has no failure case.</summary>
public sealed class ServiceRejectedException : Exception
{
    /// <summary>Creates an exception carrying the policy rejection.</summary>
    public ServiceRejectedException(Failure failure, RejectionReason reason = RejectionReason.Policy) : base(failure?.Message)
    {
        ArgumentNullException.ThrowIfNull(failure);
        Failure = failure;
        Reason = reason;
    }

    /// <summary>Gets the policy rejection details.</summary>
    public Failure Failure { get; }
    /// <summary>Gets the semantic policy rejection category.</summary>
    public RejectionReason Reason { get; }
}
