using NOF.Application;
using NOF.Contract;

namespace NOF.Sample.Application.RequestHandlers;

public class GetRootConfigNodes(IRequestExecutor executor) : NOFSampleService.GetRootConfigNodes
{
    public override Task<Result<GetRootConfigNodesResponse>> HandleAsync(GetRootConfigNodesRequest request, Context context, CancellationToken cancellationToken)
        => ExecuteUseCaseAsync<GetRootConfigNodesUseCase, GetRootConfigNodesResponse>(executor, request, context,
            static response => Result.Success(response), cancellationToken);
}
