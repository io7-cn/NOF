namespace NOF.Contract;

/// <summary>Default data carried by a declared expected failure.</summary>
public sealed record Error
{
    /// <summary>Creates error data with a user-facing message.</summary>
    public Error(string message)
    {
        ArgumentNullException.ThrowIfNull(message);
        Message = message;
    }

    /// <summary>Gets the user-facing message.</summary>
    public string Message { get; }
}
