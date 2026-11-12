using Microsoft.Extensions.Caching.Distributed;
using NOF.Application;
using NOF.Contract;
using NOF.Sample.Application.CacheKeys;
using Outcome = NOF.Sample.INOFSampleService.RemoveConfigFileResult;

namespace NOF.Sample.Application.RequestHandlers;

public class RemoveConfigFile : NOFSampleService.RemoveConfigFile
{
    private readonly IDbContext _dbContext;
    private readonly ICacheService _cache;

    public RemoveConfigFile(IDbContext dbContext, ICacheService cache)
    {
        _dbContext = dbContext;
        _cache = cache;
    }

    public override async Task<Outcome> HandleAsync(RemoveConfigFileRequest request, Context context, CancellationToken cancellationToken)
    {
        var id = ConfigNodeId.Of(request.NodeId);
        var node = await _dbContext.Set<ConfigNode>()
            .FirstOrDefaultAsync(configNode => configNode.Id == id, cancellationToken);

        if (node is null)
        {
            return new Outcome.NotFound("Node not found.");
        }

        if (!ConfigFileName.IsValid(request.FileName))
        {
            return new Outcome.InvalidInput("File name must contain 1–100 nonblank characters.");
        }
        var fileName = ConfigFileName.Of(request.FileName);
        node.RemoveConfigFile(fileName);
        await _dbContext.SaveChangesAsync(cancellationToken);

        // 写后删：清除相关缓存
        await _cache.RemoveAsync(new ConfigNodeByIdCacheKey(id), cancellationToken);
        await _cache.RemoveAsync(new ConfigNodeByNameCacheKey(node.Name), cancellationToken);
        var version = DateTime.UtcNow.Ticks;
        await _cache.SetAsync(
            new ConfigNodeVersionCacheKey(id),
            version,
            new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromDays(30) },
            cancellationToken);

        return Empty.Instance;
    }
}
