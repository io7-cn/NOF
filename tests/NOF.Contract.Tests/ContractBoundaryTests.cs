using Xunit;

namespace NOF.Contract.Tests;

public sealed class ContractBoundaryTests
{
    [Fact]
    public void Contract_ReferencesOnlyFoundation_AndHasNoForwardedExecutionTypes()
    {
        var assembly = typeof(IApplicationService).Assembly;
        Assert.Equal(["NOF.Abstraction"], assembly.GetReferencedAssemblies()
            .Where(reference => reference.Name!.StartsWith("NOF.", StringComparison.Ordinal)).Select(reference => reference.Name));
        Assert.Empty(assembly.GetForwardedTypes());
        foreach (var name in new[] { "Context", "ServiceOperation", "ServiceOperationDescriptor", "ServiceOperationReference", "ServiceOperationReference`1", "InvocationContext", "InvocationOrigin", "ServiceExecution", "ServiceCompleted", "ServiceRejected", "AuthorizationFailureReason", "ServiceRejectedException" })
        {
            Assert.Null(assembly.GetType("NOF.Contract." + name));
        }
    }
}
