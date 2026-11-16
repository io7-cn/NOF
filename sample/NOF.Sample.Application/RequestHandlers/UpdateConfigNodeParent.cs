using Microsoft.Extensions.Caching.Distributed;
using NOF.Application;
using NOF.Contract;
using NOF.Sample.Application.CacheKeys;

namespace NOF.Sample.Application.RequestHandlers;

public class UpdateConfigNodeParent : NOFSampleService.UpdateConfigNodeParent
{
    private readonly IConfigNodeRepository _nodes;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICacheService _cache;

    public UpdateConfigNodeParent(
        IConfigNodeRepository nodes, IUnitOfWork unitOfWork,
        ICacheService cache)
    {
        _nodes = nodes;
        _unitOfWork = unitOfWork;
        _cache = cache;
    }

    public override async Task<Result> HandleAsync(UpdateConfigNodeParentRequest request, InvocationContext context, CancellationToken cancellationToken)
    {
        var nodeId = ConfigNodeId.From(request.NodeId);
        var node = await _nodes.GetAsync(nodeId, cancellationToken);

        if (node is null)
        {
            return new NotFound("Node not found.");
        }

        var newParentId = request.NewParentId.HasValue
            ? ConfigNodeId.From(request.NewParentId.Value)
            : (ConfigNodeId?)null;

        // Check whether the target parent exists (unless moving to root).
        if (newParentId.HasValue)
        {
            if (newParentId.Value == nodeId)
            {
                return new ParentCycle("Cannot move a node under itself.");
            }
            var parentNode = await _nodes.GetAsync(newParentId.Value, cancellationToken);
            if (parentNode is null)
            {
                return new ParentNotFound("Target parent node not found.");
            }

            // Prevent cyclic parent relationship.
            if (await IsDescendant(nodeId, newParentId.Value, cancellationToken))
            {
                return new ParentCycle("Cannot move a node under its descendant.");
            }
        }

        node.UpdateParent(newParentId);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        // 写后删：清除相关缓存
        await _cache.RemoveAsync(new ConfigNodeByIdCacheKey(nodeId), cancellationToken);
        var version = DateTime.UtcNow.Ticks;
        await _cache.SetAsync(
            new ConfigNodeVersionCacheKey(nodeId),
            version,
            new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromDays(30) },
            cancellationToken);

        return Empty.Instance;
    }

    private async Task<bool> IsDescendant(ConfigNodeId ancestorId, ConfigNodeId nodeId, CancellationToken cancellationToken)
    {
        var current = await _nodes.GetAsync(nodeId, cancellationToken);

        while (current?.ParentId != null)
        {
            if (current.ParentId == ancestorId)
            {
                return true;
            }
            current = await _nodes.GetAsync(current.ParentId.Value, cancellationToken);
        }

        return false;
    }
}
