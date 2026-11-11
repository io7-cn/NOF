namespace NOF.Contract;

/// <summary>The stable error payload of a failed result.</summary>
public sealed record ResultError
{
    /// <summary>Creates validated error details.</summary>
    public ResultError(string code, string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentNullException.ThrowIfNull(message);
        Code = code;
        Message = message;
    }

    /// <summary>Gets the stable application error code.</summary>
    public string Code { get; }

    /// <summary>Gets the user-facing error message.</summary>
    public string Message { get; }
}
