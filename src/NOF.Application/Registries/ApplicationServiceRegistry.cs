using NOF.Abstraction;
using System.Diagnostics.CodeAnalysis;

namespace NOF.Application;

public sealed class ApplicationServiceRegistry : Registry<ApplicationServiceRegistration>
{
    private readonly Dictionary<Type, OperationMapping> _directOperations = [];

    /// <summary>Registers a statically described standalone handler without constructing it.</summary>
    public void AddOperation(OperationMapping mapping)
    {
        ArgumentNullException.ThrowIfNull(mapping);
        if (Items.IsFrozen)
        {
            throw new InvalidOperationException("Application operation registrations are frozen.");
        }
        if (mapping.Descriptor is not { } descriptor || descriptor.ContractType is not null
            || descriptor.DeclaringType != mapping.HandlerType || descriptor.RequestType != mapping.RequestType
            || descriptor.ResponseType != mapping.ReturnType)
        {
            throw new ArgumentException("A standalone mapping must describe its own handler and input/output types.", nameof(mapping));
        }
        if (!_directOperations.TryAdd(mapping.HandlerType, mapping))
        {
            throw new InvalidOperationException($"Handler '{mapping.HandlerType}' is already registered.");
        }
    }

    /// <summary>Looks up a generated operation base or a standalone handler during host configuration.</summary>
    public OperationDescriptor GetOperation(Type handlerType)
    {
        ArgumentNullException.ThrowIfNull(handlerType);
        if (_directOperations.TryGetValue(handlerType, out var direct))
        {
            return direct.Descriptor!;
        }
        return Items.SelectMany(item => item.HandlerMappings.Values)
            .SingleOrDefault(mapping => mapping.HandlerType == handlerType
                || GetOperationImplementation(mapping.HandlerType) == handlerType)?.Descriptor
            ?? throw new InvalidOperationException($"Handler '{handlerType}' is not registered. Add its Application part and, for Contract operations, its application service.");
    }

    /// <summary>Resolves frozen execution metadata for either declaration style.</summary>
    public OperationMapping GetMapping(OperationDescriptor operation)
    {
        Freeze();
        if (operation.ContractType is { } contract)
        {
            return GetRegistration(contract).HandlerMappings.GetValueOrDefault(operation.OperationName)
                ?? throw new InvalidOperationException($"No handler mapping for '{operation.Id}'.");
        }
        return _directOperations.GetValueOrDefault(operation.DeclaringType)
            ?? throw new InvalidOperationException($"No handler mapping for '{operation.Id}'.");
    }

    /// <summary>Gets every captured descriptor and freezes further operation registration.</summary>
    public IEnumerable<OperationDescriptor> GetOperations()
        => Freeze().SelectMany(item => item.HandlerMappings.Values).Concat(_directOperations.Values)
            .Select(mapping => mapping.Descriptor).OfType<OperationDescriptor>();

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
