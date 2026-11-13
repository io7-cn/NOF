using NOF.Abstraction;
using System.Diagnostics.CodeAnalysis;

namespace NOF.Application;

public sealed class ApplicationServiceRegistry : Registry<ApplicationServiceRegistration>
{
    private readonly Dictionary<Type, Type> _registrationsByService = [];

    private readonly Dictionary<Type, OperationImplementation> _operationImplementations = [];

    /// <summary>Captures a generated DI binding's actual handler type without constructing it.</summary>
    public void AddOperationImplementation(Type operationType,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicMethods | DynamicallyAccessedMemberTypes.NonPublicMethods)] Type implementationType)
    {
        if (Items.IsFrozen)
        {
            throw new InvalidOperationException("Application service registrations are frozen.");
        }
        _operationImplementations[operationType] = new(implementationType);
    }

    /// <summary>Gets the actual statically registered implementation, or the declared handler for manual mappings.</summary>
    [return: DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicMethods | DynamicallyAccessedMemberTypes.NonPublicMethods)]
    public Type GetOperationImplementation(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicMethods | DynamicallyAccessedMemberTypes.NonPublicMethods)] Type operationType)
        => _operationImplementations.TryGetValue(operationType, out var entry) ? entry.ImplementationType : operationType;

    // Private value metadata keeps linker annotations attached to each dictionary value.
    private readonly record struct OperationImplementation(
        [property: DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicMethods | DynamicallyAccessedMemberTypes.NonPublicMethods)]
        [param: DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicMethods | DynamicallyAccessedMemberTypes.NonPublicMethods)] Type ImplementationType);

    public ApplicationServiceRegistry()
    {
        Frozen += BuildIndexes;
    }

    public bool TryGetImplementationType(Type contractType, [MaybeNullWhen(false)] out Type implementationType)
    {
        ArgumentNullException.ThrowIfNull(contractType);
        Freeze();
        return _registrationsByService.TryGetValue(contractType, out implementationType);
    }

    /// <summary>Gets frozen metadata captured at service registration.</summary>
    public ApplicationServiceRegistration GetRegistration(Type contractType)
        => Freeze().SingleOrDefault(registration => registration.ServiceType == contractType)
            ?? throw new InvalidOperationException($"Application service '{contractType}' is not registered.");

    private void BuildIndexes()
    {
        _registrationsByService.Clear();
        foreach (var registration in Items)
        {
            Index(registration);
        }
    }

    private void Index(ApplicationServiceRegistration registration)
    {
        _registrationsByService[registration.ServiceType] = registration.ImplementationType;
    }
}
