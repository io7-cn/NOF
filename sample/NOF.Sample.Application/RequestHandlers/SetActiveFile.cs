using Microsoft.Extensions.Caching.Distributed;
using NOF.Application;
using NOF.Contract;
using NOF.Sample.Application.CacheKeys;

namespace NOF.Sample.Application.RequestHandlers;

public class SetActiveFile : NOFSampleService.SetActiveFile
{
    private readonly IConfigNodeRepository _nodes;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICacheService _cache;

    public SetActiveFile(IConfigNodeRepository nodes, IUnitOfWork unitOfWork, ICacheService cache)
    {
        _nodes = nodes;
        _unitOfWork = unitOfWork;
        _cache = cache;
    }

    public override async Task<Result> HandleAsync(SetActiveFileRequest request, Context context, CancellationToken cancellationToken)
    {
        var id = ConfigNodeId.From(request.NodeId);
        var node = await _nodes.GetAsync(id, cancellationToken);

        if (node is null)
        {
            return new NotFound("Node not found.");
        }

        ConfigFileName? fileName = null;
        if (!string.IsNullOrEmpty(request.FileName))
        {
            if (!ConfigFileName.TryFrom(request.FileName, out var parsedFileName, out var parsedFileNameErrorMessage))
            {
                return new InvalidInput(parsedFileNameErrorMessage);
            }
            fileName = parsedFileName;
        }
        node.SetActiveFileName(fileName);
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
