using NOF.Application;
using NOF.Contract;
using NOF.Sample.Application.CacheKeys;

namespace NOF.Sample.Application.RequestHandlers;

public class DeleteConfigNode : NOFSampleService.DeleteConfigNode
{
    private readonly IConfigNodeRepository _nodes;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICacheService _cache;
    private readonly IConfigNodeQueries _queries;

    public DeleteConfigNode(
        IConfigNodeRepository nodes, IUnitOfWork unitOfWork,
        IConfigNodeQueries queries, ICacheService cache)
    {
        _nodes = nodes;
        _unitOfWork = unitOfWork;
        _cache = cache;
        _queries = queries;
    }

    public override async Task<Result> HandleAsync(DeleteConfigNodeRequest request, Context context, CancellationToken cancellationToken)
    {
        var id = ConfigNodeId.From(request.Id);
        var node = await _nodes.GetAsync(id, cancellationToken);
        if (node is null)
        {
            return new NotFound("Node not found.");
        }

        // 检查是否有子节点
        var hasChildren = await _queries.HasChildrenAsync(id, cancellationToken);
        if (hasChildren)
        {
            return new HasChildren(new NodeHasChildrenError(request.Id));
        }

        node.MarkAsDeleted();
        _nodes.Remove(node);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        // 写后删：清除相关缓存
        await _cache.RemoveAsync(new ConfigNodeByIdCacheKey(id), cancellationToken);
        await _cache.RemoveAsync(new ConfigNodeByNameCacheKey(node.Name), cancellationToken);

        return Empty.Instance;
    }
}
