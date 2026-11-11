using NOF.Abstraction;

namespace NOF.Contract;

/// <summary>Reports a rejected service invocation when its business response has no failure case.</summary>
public sealed class ServiceRejectedException : Exception
{
    /// <summary>Creates an exception carrying the policy rejection.</summary>
    public ServiceRejectedException(Failure failure) : base(failure?.Message)
    {
        ArgumentNullException.ThrowIfNull(failure);
        Failure = failure;
    }

    /// <summary>Gets the policy rejection details.</summary>
    public Failure Failure { get; }
}
