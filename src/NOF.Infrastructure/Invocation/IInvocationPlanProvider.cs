using NOF.Application;

namespace NOF.Infrastructure;

/// <summary>Describes the actual execution plan selected by the host.</summary>
public interface IInvocationPlanProvider
{
    /// <summary>Describes an operation without constructing its filters or business handler.</summary>
    InvocationPlan Describe(OperationDescriptor operation, InvocationOrigin origin);
}
