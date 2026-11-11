using NOF.Abstraction;
using Xunit;

namespace NOF.Domain.Tests;

public class FailureModelTests
{
    [Fact]
    public void SharedFailure_ThrowsDomainExceptionWithoutAbstractionDependingOnDomain()
    {
        var failure = new Failure("order.not_found", "Missing");
        var exception = Assert.Throws<DomainException>(() => failure.Throw());
        Assert.Equal("order.not_found", exception.ErrorCode);
        Assert.Equal("Missing", exception.Message);
        Assert.Equal("NOF.Abstraction", typeof(Failure).Assembly.GetName().Name);
        Assert.Null(typeof(DomainException).Assembly.GetType("NOF.Domain.Failure"));
    }

    [Fact]
    public void DomainExceptions_PreserveSharedFailureAndInnerException()
    {
        var failure = new Failure("invalid", "Invalid order");
        var inner = new InvalidOperationException("cause");
        var exception = new DomainValidationException(failure, inner);
        Assert.Equal(failure.Code, exception.ErrorCode);
        Assert.Equal(failure.Message, exception.Message);
        Assert.Same(inner, exception.InnerException);
    }
}
