using NOF.Abstraction;
using NOF.Contract;

namespace NOF.Testing;

/// <summary>Test-owned union exercising arbitrary operation responses.</summary>
public union TestOutcome(Empty, Failure)
{
    public static TestOutcome Success() => Empty.Instance;
    public static TestOutcome<T> Success<T>(T value) => new(value);
    public static Failure Fail(string code, string message) => new(code, message);
    public static Failure Fail(Failure failure) => failure;
    public static TestOutcome CreateFailure(Failure failure) => failure;
}

/// <summary>Test-owned payload union, never shipped as framework API.</summary>
public union TestOutcome<T>(T, Failure)
{
    public static TestOutcome<T> CreateFailure(Failure failure) => failure;
}
