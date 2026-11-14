using Microsoft.Extensions.Caching.Distributed;
using NOF.Application;
using NOF.Contract;
using NOF.Sample.Application.CacheKeys;

namespace NOF.Sample.Application.RequestHandlers;

public class CreateConfigNode : NOFSampleService.CreateConfigNode
{
    private readonly IConfigNodeRepository _nodes;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICacheService _cache;

    public CreateConfigNode(
        IConfigNodeRepository nodes, IUnitOfWork unitOfWork,
        ICacheService cache)
    {
        _nodes = nodes;
        _unitOfWork = unitOfWork;
        _cache = cache;
    }

    public override async Task<Result> HandleAsync(CreateConfigNodeRequest request, InvocationContext context, CancellationToken cancellationToken)
    {
        if (!ConfigNodeName.TryFrom(request.Name, out var name, out var nameErrorMessage))
        {
            return new InvalidInput(nameErrorMessage);
        }
        var parentId = request.ParentId.HasValue ? ConfigNodeId.From(request.ParentId.Value) : (ConfigNodeId?)null;

        if (await _nodes.ExistsByNameAsync(name, cancellationToken))
        {
            return new NameConflict("Node with same name already exists.");
        }

        if (parentId.HasValue && !await _nodes.ExistsAsync(parentId.Value, cancellationToken))
        {
            return new ParentNotFound("Parent node not found.");
        }

        var node = ConfigNode.Create(name, parentId);
        _nodes.Add(node);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        // 写后删：清除相关缓存
        await _cache.RemoveAsync(new ConfigNodeByNameCacheKey(name), cancellationToken);
        var version = DateTime.UtcNow.Ticks;
        await _cache.SetAsync(new ConfigNodeVersionCacheKey(node.Id),
            version,
            new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromDays(30) },
            cancellationToken);

        return new CreatedConfigNode((long)node.Id);
    }
}
