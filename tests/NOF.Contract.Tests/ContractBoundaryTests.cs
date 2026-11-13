using Xunit;

namespace NOF.Contract.Tests;

public sealed class ContractBoundaryTests
{
    [Fact]
    public void Contract_HasNoFrameworkRuntimeDependencyOrForwardedExecutionTypes()
    {
        var assembly = typeof(IApplicationService).Assembly;
        Assert.DoesNotContain(assembly.GetReferencedAssemblies(), reference => reference.Name!.StartsWith("NOF.", StringComparison.Ordinal));
        Assert.Empty(assembly.GetForwardedTypes());
        foreach (var name in new[] { "Context", "ServiceOperation", "ServiceOperationDescriptor", "ServiceOperationReference", "ServiceOperationReference`1", "InvocationContext", "InvocationOrigin", "ServiceExecution", "ServiceCompleted", "ServiceRejected", "RejectionReason", "ServiceRejectedException" })
        {
            Assert.Null(assembly.GetType("NOF.Contract." + name));
        }
    }
}
