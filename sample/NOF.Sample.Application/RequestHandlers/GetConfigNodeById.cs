using Microsoft.Extensions.Caching.Distributed;
using NOF.Application;
using NOF.Contract;
using NOF.Sample.Application.CacheKeys;

namespace NOF.Sample.Application.RequestHandlers;

public class GetConfigNodeById : NOFSampleService.GetConfigNodeById
{
    private readonly IConfigNodeQueries _queries;
    private readonly ICacheService _cache;

    public GetConfigNodeById(IConfigNodeQueries queries, ICacheService cache)
    {
        _queries = queries;
        _cache = cache;
    }

    public override async Task<Result> HandleAsync(GetConfigNodeByIdRequest request, InvocationContext context, CancellationToken cancellationToken)
    {
        var nodeId = ConfigNodeId.From(request.Id);
        var cacheKey = new ConfigNodeByIdCacheKey(nodeId);

        var cachedValue = await _cache.GetAsync(cacheKey, cancellationToken: cancellationToken);
        if (cachedValue.HasValue)
        {
            return new GetConfigNodeByIdResponse
            {
                Node = cachedValue.Value
            };
        }

        var dto = await _queries.FindAsync(nodeId, cancellationToken);

        if (dto is null)
        {
            return new NotFound("Config node not found.");
        }

        await _cache.SetAsync(
            cacheKey,
            dto,
            new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10) },
            cancellationToken);

        return new GetConfigNodeByIdResponse
        {
            Node = dto
        };
    }
}
