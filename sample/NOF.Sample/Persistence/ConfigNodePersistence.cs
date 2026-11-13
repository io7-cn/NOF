using Microsoft.Extensions.DependencyInjection;
using NOF.Abstraction;
using NOF.Application;
using NOF.Infrastructure;
using NOF.Sample.Application;
using NOF.Sample.Application.Entities;

namespace NOF.Sample.Persistence;

/// <summary>Infrastructure implementation of aggregate and read-model ports, sharing the scoped provider session.</summary>
[AutoInject(ServiceLifetime.Scoped)]
public sealed class ConfigNodePersistence(IPersistenceSession session) : IConfigNodeRepository, IConfigNodeQueries, IConfigNodeHierarchyStore
{
    public Task<ConfigNode?> GetAsync(ConfigNodeId id, CancellationToken cancellationToken = default)
        => session.Set<ConfigNode>().FirstOrDefaultAsync(node => node.Id == id, cancellationToken);
    public Task<bool> ExistsAsync(ConfigNodeId id, CancellationToken cancellationToken = default)
        => session.Set<ConfigNode>().AsNoTracking().AnyAsync(node => node.Id == id, cancellationToken);
    public Task<bool> ExistsByNameAsync(ConfigNodeName name, CancellationToken cancellationToken = default)
        => session.Set<ConfigNode>().AsNoTracking().AnyAsync(node => node.Name == name, cancellationToken);
    public void Add(ConfigNode node) => session.Set<ConfigNode>().Add(node);
    public void Remove(ConfigNode node) => session.Set<ConfigNode>().Remove(node);
    public Task<ConfigNodeDto?> FindAsync(ConfigNodeId id, CancellationToken cancellationToken = default)
        => session.Set<ConfigNode>().AsNoTracking().Where(node => node.Id == id)
            .ProjectTo<ConfigNodeDto>().FirstOrDefaultAsync(cancellationToken);
    public Task<ConfigNodeDto?> FindByNameAsync(ConfigNodeName name, CancellationToken cancellationToken = default)
        => session.Set<ConfigNode>().AsNoTracking().Where(node => node.Name == name)
            .ProjectTo<ConfigNodeDto>().FirstOrDefaultAsync(cancellationToken);
    public Task<List<ConfigNodeDto>> GetRootsAsync(CancellationToken cancellationToken = default)
        => session.Set<ConfigNode>().AsNoTracking().Where(node => node.ParentId == null)
            .ProjectTo<ConfigNodeDto>().ToListAsync(cancellationToken);
    public async Task<GetConfigNodeChildrenResponse?> GetChildrenAsync(ConfigNodeId id, CancellationToken cancellationToken = default)
    {
        var children = await session.Set<ConfigNodeChildren>().AsNoTracking().FirstOrDefaultAsync(node => node.NodeId == id, cancellationToken);
        return children is null ? null : new GetConfigNodeChildrenResponse { NodeId = (long)id, ChildrenIds = children.ChildrenIds.ToList() };
    }
    public async Task<bool> HasChildrenAsync(ConfigNodeId id, CancellationToken cancellationToken = default)
        => (await session.Set<ConfigNodeChildren>().AsNoTracking().FirstOrDefaultAsync(node => node.NodeId == id, cancellationToken))?.HasChildren() ?? false;
    Task<ConfigNodeChildren?> IConfigNodeHierarchyStore.GetAsync(ConfigNodeId id, CancellationToken cancellationToken)
        => session.Set<ConfigNodeChildren>().FirstOrDefaultAsync(node => node.NodeId == id, cancellationToken);
    public void Add(ConfigNodeChildren node) => session.Set<ConfigNodeChildren>().Add(node);
    public void Remove(ConfigNodeChildren node) => session.Set<ConfigNodeChildren>().Remove(node);
}
