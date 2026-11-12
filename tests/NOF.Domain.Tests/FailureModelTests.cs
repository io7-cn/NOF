using NOF.Abstraction;
using Xunit;

namespace NOF.Domain.Tests;

public class FailureModelTests
{
    [Fact]
    public void ExpectedFailures_HaveNoDomainExceptionConversion()
    {
        var assembly = typeof(DomainException).Assembly;
        Assert.Null(assembly.GetType("NOF.Domain.FailureAttribute"));
        Assert.Null(assembly.GetType("NOF.Domain.NOFDomainExtensions"));
        Assert.DoesNotContain(typeof(DomainException).GetConstructors(), constructor =>
            constructor.GetParameters().Any(parameter => parameter.ParameterType == typeof(Failure)));
    }

    [Fact]
    public void InvariantExceptions_PreserveCodeMessageAndInnerException()
    {
        var inner = new InvalidOperationException("cause");
        var exception = new DomainValidationException("invalid", "Invalid order", inner);
        Assert.Equal("invalid", exception.ErrorCode);
        Assert.Equal("Invalid order", exception.Message);
        Assert.Same(inner, exception.InnerException);
    }
}
