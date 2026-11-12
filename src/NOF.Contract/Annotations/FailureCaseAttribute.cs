namespace NOF.Contract;

/// <summary>Declares one expected failure code on an application service operation.</summary>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
public class FailureCaseAttribute(string code) : Attribute
{
    /// <summary>Gets the stable failure code.</summary>
    public string Code { get; } = code;

    /// <summary>Gets or sets the C# case name, independently of Code. Required for multiple failures; a single unnamed case is Failed.</summary>
    public string? Name { get; set; }
}

/// <summary>Declares an expected failure carrying application-defined error data.</summary>
/// <typeparam name="TError">The error data type.</typeparam>
[AttributeUsage(AttributeTargets.Method, AllowMultiple = true, Inherited = false)]
public sealed class FailureCaseAttribute<TError>(string code) : FailureCaseAttribute(code);
