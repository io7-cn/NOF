using NOF.Sample.Application.Entities;

namespace NOF.Sample.Application;

/// <summary>Maintains the hierarchy projection in the same unit of work as the aggregate events.</summary>
public interface IConfigNodeHierarchyStore
{
    Task<ConfigNodeChildren?> GetAsync(ConfigNodeId id, CancellationToken cancellationToken = default);
    void Add(ConfigNodeChildren node);
    void Remove(ConfigNodeChildren node);
}
