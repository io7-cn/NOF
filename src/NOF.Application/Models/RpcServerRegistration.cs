namespace NOF.Application;

public sealed record RpcServerRegistration(
    Type ServiceType,
    Type ImplementationType)
{
    /// <summary>Gets the operations captured by generic server registration.</summary>
    public IReadOnlyDictionary<string, RpcHandlerMapping>? HandlerMappings { get; init; }
}
