using NOF.Application;
using NOF.Contract;
using NOF.Sample.Application.Repositories;

namespace NOF.Sample.Application.RequestHandlers;

public class GetRootConfigNodesUseCase(IDbContext dbContext)
    : RequestHandler<GetRootConfigNodesRequest, GetRootConfigNodesResponse>
{
    public override async Task<GetRootConfigNodesResponse> HandleAsync(
        GetRootConfigNodesRequest request, Context context, CancellationToken cancellationToken)
    {
        var nodes = await dbContext.Set<ConfigNode>()
            .QueryRootNodes()
            .ProjectTo<ConfigNodeDto>()
            .ToListAsync(cancellationToken);
        return new GetRootConfigNodesResponse { Nodes = nodes };
    }
}
