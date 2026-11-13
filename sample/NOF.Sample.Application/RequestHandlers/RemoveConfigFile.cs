using Microsoft.Extensions.Caching.Distributed;
using NOF.Application;
using NOF.Contract;
using NOF.Sample.Application.CacheKeys;

namespace NOF.Sample.Application.RequestHandlers;

public class RemoveConfigFile : NOFSampleService.RemoveConfigFile
{
    private readonly IConfigNodeRepository _nodes;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICacheService _cache;

    public RemoveConfigFile(IConfigNodeRepository nodes, IUnitOfWork unitOfWork, ICacheService cache)
    {
        _nodes = nodes;
        _unitOfWork = unitOfWork;
        _cache = cache;
    }

    public override async Task<Result> HandleAsync(RemoveConfigFileRequest request, Context context, CancellationToken cancellationToken)
    {
        var id = ConfigNodeId.From(request.NodeId);
        var node = await _nodes.GetAsync(id, cancellationToken);

        if (node is null)
        {
            return new NotFound("Node not found.");
        }

        if (!ConfigFileName.TryFrom(request.FileName, out var fileName, out var fileNameErrorMessage))
        {
            return new InvalidInput(fileNameErrorMessage);
        }
        node.RemoveConfigFile(fileName);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

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
