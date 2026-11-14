using NOF.Application;
using System.Collections.Concurrent;

namespace NOF.Infrastructure;

/// <summary>Frozen host configuration used only for description, never request identity.</summary>
public sealed record InvocationPlanHostConfiguration(bool CustomAuthorization,
    bool RequireAuthenticatedTenant, bool RejectMismatchedTenant, bool UsesDefaultEngine);

/// <summary>Builds and caches execution and description from the same effective rule set.</summary>
public sealed class InvocationPlanFactory(InvocationFilterRegistry rules, ApplicationServiceRegistry services,
    InvocationPlanHostConfiguration configuration)
{
    private readonly ConcurrentDictionary<(ServiceOperationDescriptor, Type, InvocationOrigin), InvocationPlan> _plans = new();
    /// <summary>Describes captured registration metadata without resolving scoped application services.</summary>
    public InvocationPlan Describe(ServiceOperationDescriptor operation, InvocationOrigin origin)
    {
        var registration = services.GetRegistration(operation.ServiceType);
        var mapping = registration.HandlerMappings.GetValueOrDefault(operation.OperationName)
            ?? throw new InvalidOperationException("Construction-free invocation description requires captured handler mappings.");
        var implementation = services.GetOperationImplementation(mapping.HandlerType);
        return Describe(new InvocationContext
        {
            Operation = operation,
            Origin = origin,
            HandlerType = implementation,
            HandlerMethodInfo = InboundContextReflection.ResolveHandlerMethodInfo(implementation, mapping.RequestType)
        });
    }
    /// <summary>Builds a plan from already resolved operation/implementation metadata.</summary>
    public InvocationPlan Describe(InvocationContext context)
        => _plans.GetOrAdd((context.Operation, context.HandlerType, context.Origin), _ => Build(context));
    private InvocationPlan Build(InvocationContext context)
    {
        var filters = rules.Resolve(context.ServiceType, context.Operation.Id);
        var policy = InvocationAuthorizationPolicy.Resolve(context.ServiceType, context.ServiceMethodInfo,
            context.HandlerType, context.HandlerMethodInfo, new TenantAuthorizationOptions
            {
                RequireAuthenticatedTenant = configuration.RequireAuthenticatedTenant,
                RejectMismatchedTenant = configuration.RejectMismatchedTenant
            }, configuration.CustomAuthorization);
        var failures = new List<InvocationFailureCase>();
        foreach (var filter in filters)
        {
            var description = new InvocationDescriptionContext(context, filter.FailureCases, [policy]);
            filter.Describe(description);
            failures.AddRange(description.Included);
        }
        return new(context.Operation, context.Origin, filters, failures);
    }
    /// <summary>Validates rule conflicts for every known operation before the selected engine starts serving.</summary>
    public void ValidateConfiguration()
    {
        if (!configuration.UsesDefaultEngine)
        {
            return;
        }
        foreach (var registration in services.Freeze())
        {
            foreach (var entry in registration.HandlerMappings ?? new Dictionary<string, ServiceOperationMapping>())
            {
                _ = rules.Resolve(registration.ServiceType, $"{registration.ServiceType.FullName}/{entry.Key}");
                if (entry.Value.Descriptor is { } descriptor)
                {
                    foreach (var origin in Enum.GetValues<InvocationOrigin>())
                    {
                        _ = Describe(descriptor, origin);
                    }
                }
            }
        }
    }
}
