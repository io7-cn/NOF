using NOF.Application;
using NOF.Contract;
using NOF.Sample.Application.Entities;
using Outcome = NOF.Sample.INOFSampleService.GetConfigNodeChildrenResult;

namespace NOF.Sample.Application.RequestHandlers;

public class GetConfigNodeChildren : NOFSampleService.GetConfigNodeChildren
{
    private readonly IDbContext _dbContext;

    public GetConfigNodeChildren(IDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public override async Task<Outcome> HandleAsync(GetConfigNodeChildrenRequest request, Context context, CancellationToken cancellationToken)
    {
        var nodeId = ConfigNodeId.Of(request.Id);
        var children = await _dbContext.Set<ConfigNodeChildren>()
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.NodeId == nodeId, cancellationToken);

        if (children is null)
        {
            return new Outcome.NotFound("未找到子节点信息");
        }

        return new GetConfigNodeChildrenResponse
        {
            NodeId = (long)children.NodeId,
            ChildrenIds = children.ChildrenIds
        };
    }
}
