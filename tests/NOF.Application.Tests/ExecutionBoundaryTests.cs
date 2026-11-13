using NOF.Abstraction;
using NOF.Contract;
using Xunit;

namespace NOF.Application.Tests;

public sealed class ExecutionBoundaryTests
{
    [Fact]
    public void ExecutionModels_BelongToApplication_AndSemanticContextToAbstraction()
    {
        foreach (var type in new[] { typeof(ServiceOperation), typeof(ServiceOperation<,>), typeof(InvocationContext), typeof(InvocationOrigin), typeof(ServiceOperationDescriptor), typeof(ServiceOperationDescriptor<,>), typeof(ServiceExecution), typeof(ServiceCompleted), typeof(ServiceRejected), typeof(RejectionReason), typeof(ServiceRejectedException) })
        {
            Assert.Equal(typeof(ApplicationService).Assembly, type.Assembly);
            Assert.Equal("NOF.Application", type.Namespace);
        }
        Assert.Equal(typeof(Failure).Assembly, typeof(Context).Assembly);
        Assert.Equal("NOF.Abstraction", typeof(Context).Namespace);
    }

    [Fact]
    public void ReferenceValidation_IsDeferredToApplication()
    {
        var missing = new ServiceOperationReference<Query>(typeof(IService), "Missing");
        Assert.Throws<ArgumentException>(() => ServiceOperationInspector.ValidateReference(missing));
        var wrongInput = new ServiceOperationReference<object>(typeof(IService), nameof(IService.Run));
        Assert.Throws<ArgumentException>(() => ServiceOperationInspector.ValidateReference(wrongInput));
        Assert.Throws<ArgumentException>(() => new ServiceOperationDescriptor<object, Empty>(typeof(IService), nameof(IService.Run)));
        Assert.True(ServiceOperationInspector.CanSubmit(new ServiceOperationReference<Query>(typeof(IService), nameof(IService.Run))));
        Assert.False(ServiceOperationInspector.CanSubmit(new ServiceOperationReference<Query>(typeof(IService), nameof(IService.MayFail))));
    }

    [Fact]
    public void PolicyMetadata_IsInterpretedByApplication()
    {
        Assert.True(ServiceOperationInspector.RequiresHostTenant(typeof(IService).GetMethod(nameof(IService.Host))));
        Assert.False(ServiceOperationInspector.RequiresHostTenant(typeof(IService).GetMethod(nameof(IService.Run))));
    }

    public sealed record Query;
    public interface IService : IApplicationService
    {
        Empty Run(Query input);
        [FailureCase("Rejected")]
        Empty MayFail(Query input);
        [UseHostTenant]
        Empty Host(Query input);
    }
}
