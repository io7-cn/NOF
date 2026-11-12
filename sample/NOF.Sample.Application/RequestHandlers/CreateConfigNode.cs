using Microsoft.Extensions.Caching.Distributed;
using NOF.Application;
using NOF.Contract;
using NOF.Sample.Application.CacheKeys;
using NOF.Sample.Application.Repositories;

namespace NOF.Sample.Application.RequestHandlers;

public class CreateConfigNode : NOFSampleService.CreateConfigNode
{
    private readonly IDbContext _dbContext;
    private readonly ICacheService _cache;

    public CreateConfigNode(
        IDbContext dbContext,
        ICacheService cache)
    {
        _dbContext = dbContext;
        _cache = cache;
    }

    public override async Task<Result> HandleAsync(CreateConfigNodeRequest request, Context context, CancellationToken cancellationToken)
    {
        if (!ConfigNodeName.IsValid(request.Name))
        {
            return new InvalidInput("Node name must contain 1–100 nonblank characters.");
        }
        var name = ConfigNodeName.Of(request.Name);
        var parentId = request.ParentId.HasValue ? ConfigNodeId.Of(request.ParentId.Value) : (ConfigNodeId?)null;

        if (await _dbContext.Set<ConfigNode>().ExistsByNameAsync(name, cancellationToken))
        {
            return new NameConflict("Node with same name already exists.");
        }

        if (parentId.HasValue && !await _dbContext.Set<ConfigNode>()
                .AnyAsync(node => node.Id == parentId.Value, cancellationToken))
        {
            return new ParentNotFound("Parent node not found.");
        }

        var node = ConfigNode.Create(name, parentId);
        _dbContext.Set<ConfigNode>().Add(node);
        await _dbContext.SaveChangesAsync(cancellationToken);

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
