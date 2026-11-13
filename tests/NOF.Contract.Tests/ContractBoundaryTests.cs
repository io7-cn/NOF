using System.Reflection;
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
        foreach (var name in new[] { "Context", "ServiceOperation", "ServiceOperationDescriptor", "InvocationContext", "InvocationOrigin", "ServiceExecution", "ServiceCompleted", "ServiceRejected", "RejectionReason", "ServiceRejectedException" })
        {
            Assert.Null(assembly.GetType("NOF.Contract." + name));
        }
    }

    [Fact]
    public void Reference_IsDeclarationData_WithoutReflectionOrExecutionDecisions()
    {
        // Resolving this intentionally unimplemented declaration belongs to Application.
        var reference = new ServiceOperationReference<object>(typeof(IDeclaration), "NotImplemented");
        Assert.Equal(typeof(IDeclaration), reference.ServiceType);
        Assert.Equal("NotImplemented", reference.OperationName);
        Assert.Equal(typeof(object), reference.RequestType);
        Assert.EndsWith("/NotImplemented", reference.Id);
        Assert.Null(typeof(ServiceOperationReference).GetProperty("MethodInfo"));
        Assert.Null(typeof(ServiceOperationReference).GetProperty("CanSubmit"));
        Assert.DoesNotContain(typeof(ServiceOperationReference).GetProperties(), property => typeof(MemberInfo).IsAssignableFrom(property.PropertyType) && property.PropertyType != typeof(Type));
        Assert.Null(typeof(UseHostTenantAttribute).GetMethod("IsRequired"));
    }

    public interface IDeclaration : IApplicationService;
}
