using NOF.Application;
using NOF.Contract;
using NOF.Sample.Application.Entities;

namespace NOF.Sample.Application.RequestHandlers;

public class GetConfigNodeChildren : NOFSampleService.GetConfigNodeChildren
{
    private readonly IConfigNodeQueries _queries;

    public GetConfigNodeChildren(IConfigNodeQueries queries)
    {
        _queries = queries;
    }

    public override async Task<Result> HandleAsync(GetConfigNodeChildrenRequest request, InvocationContext context, CancellationToken cancellationToken)
    {
        var nodeId = ConfigNodeId.From(request.Id);
        var children = await _queries.GetChildrenAsync(nodeId, cancellationToken);

        if (children is null)
        {
            return new NotFound("未找到子节点信息");
        }

        return children;
    }
}
