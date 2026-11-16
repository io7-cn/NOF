using NOF.Application;
using NOF.Contract;

namespace NOF.Sample.Application.RequestHandlers;

public class GetRootConfigNodes(IConfigNodeQueries queries) : NOFSampleService.GetRootConfigNodes
{
    public override async Task<GetRootConfigNodesResponse> HandleAsync(
        GetRootConfigNodesRequest request, InvocationContext context, CancellationToken cancellationToken)
    {
        var nodes = await queries.GetRootsAsync(cancellationToken);
        return new GetRootConfigNodesResponse { Nodes = nodes };
    }
}
