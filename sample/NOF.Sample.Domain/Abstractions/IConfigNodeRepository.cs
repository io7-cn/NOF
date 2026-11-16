namespace NOF.Sample;

/// <summary>Loads and stages configuration-node aggregates within the current unit of work.</summary>
public interface IConfigNodeRepository
{
    Task<ConfigNode?> GetAsync(ConfigNodeId id, CancellationToken cancellationToken = default);
    Task<bool> ExistsAsync(ConfigNodeId id, CancellationToken cancellationToken = default);
    Task<bool> ExistsByNameAsync(ConfigNodeName name, CancellationToken cancellationToken = default);
    void Add(ConfigNode node);
    void Remove(ConfigNode node);
}
