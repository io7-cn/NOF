using Microsoft.Extensions.Caching.Distributed;
using NOF.Application;
using NOF.Contract;
using NOF.Sample.Application.CacheKeys;
using Outcome = NOF.Sample.INOFSampleService.UpdateConfigNodeParentResult;

namespace NOF.Sample.Application.RequestHandlers;

public class UpdateConfigNodeParent : NOFSampleService.UpdateConfigNodeParent
{
    private readonly IDbContext _dbContext;
    private readonly ICacheService _cache;

    public UpdateConfigNodeParent(
        IDbContext dbContext,
        ICacheService cache)
    {
        _dbContext = dbContext;
        _cache = cache;
    }

    public override async Task<Outcome> HandleAsync(UpdateConfigNodeParentRequest request, Context context, CancellationToken cancellationToken)
    {
        var nodeId = ConfigNodeId.Of(request.NodeId);
        var node = await _dbContext.Set<ConfigNode>()
            .FirstOrDefaultAsync(configNode => configNode.Id == nodeId, cancellationToken);

        if (node is null)
        {
            return new Outcome.NotFound("Node not found.");
        }

        var newParentId = request.NewParentId.HasValue
            ? ConfigNodeId.Of(request.NewParentId.Value)
            : (ConfigNodeId?)null;

        // Check whether the target parent exists (unless moving to root).
        if (newParentId.HasValue)
        {
            if (newParentId.Value == nodeId)
            {
                return new Outcome.ParentCycle("Cannot move a node under itself.");
            }
            var parentNode = await _dbContext.Set<ConfigNode>()
                .FirstOrDefaultAsync(configNode => configNode.Id == newParentId.Value, cancellationToken);
            if (parentNode is null)
            {
                return new Outcome.ParentNotFound("Target parent node not found.");
            }

            // Prevent cyclic parent relationship.
            if (await IsDescendant(nodeId, newParentId.Value, cancellationToken))
            {
                return new Outcome.ParentCycle("Cannot move a node under its descendant.");
            }
        }

        node.UpdateParent(newParentId);
        await _dbContext.SaveChangesAsync(cancellationToken);

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
        var current = await _dbContext.Set<ConfigNode>()
            .FirstOrDefaultAsync(configNode => configNode.Id == nodeId, cancellationToken);

        while (current?.ParentId != null)
        {
            if (current.ParentId == ancestorId)
            {
                return true;
            }
            current = await _dbContext.Set<ConfigNode>()
                .FirstOrDefaultAsync(configNode => configNode.Id == current.ParentId.Value, cancellationToken);
        }

        return false;
    }
}
