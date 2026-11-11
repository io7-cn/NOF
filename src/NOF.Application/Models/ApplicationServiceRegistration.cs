namespace NOF.Application;

public sealed record ApplicationServiceRegistration(
    Type ServiceType,
    Type ImplementationType)
{
    /// <summary>Gets the operations captured by generic server registration.</summary>
    public IReadOnlyDictionary<string, ServiceOperationMapping>? HandlerMappings { get; init; }
}
