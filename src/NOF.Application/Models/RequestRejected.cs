using NOF.Abstraction;

namespace NOF.Application;

/// <summary>An application policy rejected execution before a business response was produced.</summary>
public sealed record RequestRejected
{
    /// <summary>Creates a rejection with a semantic reason and shared failure details.</summary>
    public RequestRejected(RequestRejectionKind kind, Failure failure)
    {
        ArgumentNullException.ThrowIfNull(failure);
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }
        Kind = kind;
        Failure = failure;
    }

    /// <summary>Gets the semantic reason. Adapters choose the corresponding wire representation.</summary>
    public RequestRejectionKind Kind { get; }

    /// <summary>Gets the application policy failure.</summary>
    public Failure Failure { get; }
}
