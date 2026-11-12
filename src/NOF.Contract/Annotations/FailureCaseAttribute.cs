namespace NOF.Contract;

/// <summary>Declares a named expected failure on an application service operation.</summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
public sealed class FailureCaseAttribute : Attribute
{
    /// <summary>Declares a message failure whose error code defaults to its C# branch name.</summary>
    public FailureCaseAttribute(string name) : this(name, name) { }

    /// <summary>Declares a message failure with an explicit opaque error code.</summary>
    public FailureCaseAttribute(string name, string errorCode)
    {
        Name = name;
        ErrorCode = errorCode;
    }

    /// <summary>Declares a failure carrying custom error data instead of a code and message.</summary>
    public FailureCaseAttribute(string name, Type errorType)
    {
        Name = name;
        ErrorType = errorType;
    }

    /// <summary>Gets the required C# branch name, validated by the Contract generator.</summary>
    public string Name { get; }

    /// <summary>Gets the error code for a message failure, or null for custom error data.</summary>
    public string? ErrorCode { get; }

    /// <summary>Gets the custom error data type, or null for a message failure.</summary>
    public Type? ErrorType { get; }
}
