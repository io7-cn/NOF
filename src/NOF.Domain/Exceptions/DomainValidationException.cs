namespace NOF.Domain;

/// <summary>
/// Exception thrown when domain validation fails.
/// </summary>
public class DomainValidationException : DomainException
{
    /// <summary>
    /// Initializes a new instance with the default validation error code.
    /// </summary>
    /// <param name="message">The validation error message.</param>
    public DomainValidationException(string message)
        : base("400", message)
    {
    }

    /// <summary>
    /// Initializes a new instance with the default validation error code and inner exception.
    /// </summary>
    /// <param name="message">The validation error message.</param>
    /// <param name="innerException">The inner exception.</param>
    public DomainValidationException(string message, Exception innerException)
        : base("400", message, innerException)
    {
    }

    /// <summary>Initializes a new instance with an error code and message.</summary>
    /// <param name="errorCode">The error code.</param>
    /// <param name="message">The error message.</param>
    public DomainValidationException(string errorCode, string message) : base(errorCode, message)
    {
    }

    /// <summary>Initializes a new instance with an error code, message, and inner exception.</summary>
    /// <param name="errorCode">The error code.</param>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The inner exception.</param>
    public DomainValidationException(string errorCode, string message, Exception innerException) : base(errorCode, message, innerException)
    {
    }
}
