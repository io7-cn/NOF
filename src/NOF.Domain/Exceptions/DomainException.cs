namespace NOF.Domain;

/// <summary>
/// Exception thrown when a domain rule is violated.
/// </summary>
public class DomainException : Exception
{
    /// <summary>Gets the application-specific error code.</summary>
    public string ErrorCode { get; }

    /// <summary>Initializes a new instance with an error code and message.</summary>
    /// <param name="errorCode">The error code.</param>
    /// <param name="message">The error message.</param>
    public DomainException(string errorCode, string message)
        : base(message)
    {
        ErrorCode = errorCode;
    }

    /// <summary>Initializes a new instance with an error code, message, and inner exception.</summary>
    /// <param name="errorCode">The error code.</param>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The inner exception.</param>
    public DomainException(string errorCode, string message, Exception innerException)
        : base(message, innerException)
    {
        ErrorCode = errorCode;
    }
}
