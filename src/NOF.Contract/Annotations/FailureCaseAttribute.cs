namespace NOF.Contract;

/// <summary>Declares a predictable failure on a service method or partial filter.</summary>
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class FailureCaseAttribute : Attribute
{
    /// <summary>Declares a failure whose summary defaults to the branch name.</summary>
    public FailureCaseAttribute(string name) : this(name, name) { }
    /// <summary>Declares a named failure with a summary.</summary>
    public FailureCaseAttribute(string name, string title) { Name = name; Title = title; }
    /// <summary>Declares a named failure with a summary and typed error data.</summary>
    public FailureCaseAttribute(string name, string title, Type errorType) : this(name, title) { ErrorType = errorType; }
    /// <summary>Gets the required valid C# branch identifier.</summary>
    public string Name { get; }
    /// <summary>Gets the failure summary, independent of HTTP status.</summary>
    public string Title { get; }
    /// <summary>Gets optional custom error data metadata.</summary>
    public Type? ErrorType { get; }
}
