using Microsoft.Extensions.Caching.Distributed;
using NOF.Application;
using NOF.Contract;
using NOF.Sample.Application.CacheKeys;

namespace NOF.Sample.Application.RequestHandlers;

public class AddOrUpdateConfigFile : NOFSampleService.AddOrUpdateConfigFile
{
    private readonly IDbContext _dbContext;
    private readonly ICacheService _cache;

    public AddOrUpdateConfigFile(IDbContext dbContext, ICacheService cache)
    {
        _dbContext = dbContext;
        _cache = cache;
    }

    public override async Task<Result> HandleAsync(AddOrUpdateConfigFileRequest request, Context context, CancellationToken cancellationToken)
    {
        var id = ConfigNodeId.From(request.NodeId);
        var node = await _dbContext.Set<ConfigNode>()
            .FirstOrDefaultAsync(configNode => configNode.Id == id, cancellationToken);

        if (node is null)
        {
            return new NotFound("Node not found.");
        }

        if (!ConfigFileName.TryFrom(request.FileName, out var fileName))
        {
            return new InvalidInput("File name must contain 1–100 nonblank characters.");
        }
        if (!ConfigContent.TryFrom(request.Content, out var content))
        {
            return new InvalidInput("Configuration content must be a JSON object.");
        }

        node.AddOrUpdateConfigFile(fileName, content);
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
