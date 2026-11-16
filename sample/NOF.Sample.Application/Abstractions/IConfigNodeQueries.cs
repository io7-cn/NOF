using NOF.Sample.Application.Entities;

namespace NOF.Sample.Application;

/// <summary>Returns configuration read models without exposing provider query objects.</summary>
public interface IConfigNodeQueries
{
    Task<ConfigNodeDto?> FindAsync(ConfigNodeId id, CancellationToken cancellationToken = default);
    Task<ConfigNodeDto?> FindByNameAsync(ConfigNodeName name, CancellationToken cancellationToken = default);
    Task<List<ConfigNodeDto>> GetRootsAsync(CancellationToken cancellationToken = default);
    Task<GetConfigNodeChildrenResponse?> GetChildrenAsync(ConfigNodeId id, CancellationToken cancellationToken = default);
    Task<bool> HasChildrenAsync(ConfigNodeId id, CancellationToken cancellationToken = default);
}
