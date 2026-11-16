using Xunit;

namespace NOF.Domain.Tests;

public class FailureModelTests
{
    [Fact]
    public void DomainSurface_HasNoDedicatedExceptionsOrFailureConversions()
    {
        var assembly = typeof(IValueObject<>).Assembly;
        Assert.Null(assembly.GetType("NOF.Domain.DomainException"));
        Assert.Null(assembly.GetType("NOF.Domain.DomainValidationException"));
        Assert.Null(assembly.GetType("NOF.Domain.FailureAttribute"));
        Assert.Null(assembly.GetType("NOF.Domain.NOFDomainExtensions"));
    }
}
