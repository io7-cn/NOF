using NOF.Abstraction;
using NOF.Contract;
using System.Reflection;
using Xunit;

namespace NOF.Application.Tests;

public sealed class ExecutionBoundaryTests
{
    [Fact]
    public void ExecutionModels_BelongToApplication_AndSemanticContextToAbstraction()
    {
        foreach (var type in new[] { typeof(ServiceOperation), typeof(ServiceOperation<,>), typeof(InvocationContext), typeof(InvocationOrigin), typeof(ServiceOperationDescriptor), typeof(ServiceOperationDescriptor<,>) })
        {
            Assert.Equal(typeof(ApplicationService).Assembly, type.Assembly);
            Assert.Equal("NOF.Application", type.Namespace);
        }
        foreach (var name in new[] { "ServiceExecution", "ServiceCompleted", "ServiceRejected", "ServiceRejectedException", "IInvocationFilter", "IInvocationPipeline" })
        {
            Assert.Null(typeof(ApplicationService).Assembly.GetType("NOF.Application." + name));
        }
        foreach (var name in new[] { "Execution", "Response", "Rejection", "RequiredPermissions" })
        {
            Assert.Null(typeof(InvocationContext).GetProperty(name));
        }
        Assert.Equal(typeof(Failure).Assembly, typeof(Context).Assembly);
        Assert.Equal("NOF.Abstraction", typeof(Context).Namespace);
    }

    [Fact]
    public void Descriptor_ValidatesContractInput_WithoutConstructingAnImplementation()
    {
        Assert.Null(typeof(ApplicationService).Assembly.GetType("NOF.Application.ServiceOperationReference"));
        Assert.Throws<ArgumentException>(() => new ServiceOperationDescriptor<Query, Empty>(typeof(IService), "Missing"));
        Assert.Throws<ArgumentException>(() => new ServiceOperationDescriptor<object, Empty>(typeof(IService), nameof(IService.Run)));
        Assert.True(ServiceOperationInspector.CanSubmit(new ServiceOperationDescriptor<Query, Empty>(typeof(IService), nameof(IService.Run))));
        Assert.False(ServiceOperationInspector.CanSubmit(new ServiceOperationDescriptor<Query, Empty>(typeof(IService), nameof(IService.MayFail))));
    }

    public sealed record Query;
    public interface IService : IApplicationService
    {
        Empty Run(Query input);
        [FailureCase("Rejected")]
        Empty MayFail(Query input);
        Empty Host(Query input);
    }
}
