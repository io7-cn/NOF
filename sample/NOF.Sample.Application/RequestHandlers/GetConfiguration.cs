using Microsoft.Extensions.Caching.Distributed;
using NOF.Application;
using NOF.Contract;
using NOF.Sample.Application.CacheKeys;
using System.Text.Json.Nodes;

namespace NOF.Sample.Application.RequestHandlers;

public class GetConfiguration : NOFSampleService.GetConfiguration
{
    private readonly IConfigNodeQueries _queries;
    private readonly ICacheService _cache;

    public GetConfiguration(IConfigNodeQueries queries, ICacheService cache)
    {
        _queries = queries;
        _cache = cache;
    }

    public override async Task<Result> HandleAsync(GetConfigurationRequest request, Context context, CancellationToken cancellationToken)
    {
        if (!ConfigNodeName.TryFrom(request.AppName, out var appName, out var appNameErrorMessage))
        {
            return new InvalidInput(appNameErrorMessage);
        }
        var appNameStr = (string)appName;
        var appCacheKey = new ConfigResultCacheKey(appNameStr);

        // 1. Find App Node
        var appNodeDto = await GetNodeByNameAsync(appName, cancellationToken);

        if (appNodeDto is null)
        {
            return new NotFound("Config node not found.");
        }

        // 2. Expand Path to Root
        var fullPath = await ExpandPathToRoot(appNodeDto, cancellationToken);

        // Check versions
        var maxVersion = 0L;
        foreach (var versionKey in fullPath.Select(node => new ConfigNodeVersionCacheKey(ConfigNodeId.From(node.Id))))
        {
            var version = await _cache.GetAsync(versionKey, cancellationToken: cancellationToken);
            version.IfSome(versionVal =>
            {
                if (versionVal > maxVersion)
                {
                    maxVersion = versionVal;
                }
            });
        }

        var cachedResult = await _cache.GetAsync(appCacheKey, cancellationToken: cancellationToken);
        if (cachedResult.HasValue && cachedResult.Value.Version >= maxVersion)
        {
            return new GetConfigurationResponse
            {
                Content = cachedResult.Value.Content
            };
        }


        // 3. Re-compute
        var mergedJson = ComputeConfiguration(fullPath);
        var jsonString = mergedJson.ToJsonString();

        // 4. Update Result Cache
        var newResult = new CachedConfigResult(jsonString, maxVersion);
        await _cache.SetAsync(
            appCacheKey,
            newResult,
            new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromDays(7) },
            cancellationToken);

        return new GetConfigurationResponse
        {
            Content = jsonString
        };
    }

    private async Task<ConfigNodeDto?> GetNodeByNameAsync(ConfigNodeName name, CancellationToken cancellationToken)
    {
        var cacheKey = new ConfigNodeByNameCacheKey(name);
        var cachedValue = await _cache.GetAsync(cacheKey, cancellationToken: cancellationToken);
        if (cachedValue.HasValue)
        {
            return cachedValue.Value;
        }

        var dto = await _queries.FindByNameAsync(name, cancellationToken);
        if (dto is null)
        {
            return null;
        }

        await _cache.SetAsync(
            cacheKey,
            dto,
            new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10) },
            cancellationToken);

        return dto;
    }

    private async Task<ConfigNodeDto?> GetNodeByIdAsync(ConfigNodeId id, CancellationToken cancellationToken)
    {
        var cacheKey = new ConfigNodeByIdCacheKey(id);
        var cachedValue = await _cache.GetAsync(cacheKey, cancellationToken: cancellationToken);
        if (cachedValue.HasValue)
        {
            return cachedValue.Value;
        }

        var dto = await _queries.FindAsync(id, cancellationToken);
        if (dto is null)
        {
            return null;
        }

        await _cache.SetAsync(
            cacheKey,
            dto,
            new DistributedCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10) },
            cancellationToken);

        return dto;
    }

    private async Task<List<ConfigNodeDto>> ExpandPathToRoot(ConfigNodeDto appNode, CancellationToken cancellationToken)
    {
        var fullPath = new List<ConfigNodeDto>();

        // Trace up from AppNode
        var current = appNode;
        fullPath.Add(current);

        while (current.ParentId.HasValue)
        {
            var parent = await GetNodeByIdAsync(ConfigNodeId.From(current.ParentId.Value), cancellationToken);
            if (parent is null)
            {
                break;
            }
            fullPath.Add(parent);
            current = parent;
        }

        fullPath.Reverse();
        return fullPath;
    }

    private static JsonObject ComputeConfiguration(List<ConfigNodeDto> pathNodes)
    {
        var mergedJson = new JsonObject();

        // PathNodes is ordered from Root -> App
        foreach (var activeFile in pathNodes
                     .Where(node => !string.IsNullOrWhiteSpace(node.ActiveFileName))
                     .Select(node => node.ConfigFiles.FirstOrDefault(f => f.Name == node.ActiveFileName))
                     .Where(activeFile => !string.IsNullOrWhiteSpace(activeFile?.Content)))
        {
            var nodeJson = JsonNode.Parse(activeFile!.Content);
            if (nodeJson is JsonObject jsonObj)
            {
                MergeJson(mergedJson, jsonObj);
            }
        }
        return mergedJson;
    }

    private static void MergeJson(JsonObject target, JsonObject source)
    {
        foreach (var (key, sourceValue) in source)
        {
            if (target.ContainsKey(key))
            {
                var targetValue = target[key];

                if (targetValue is JsonObject targetObj && sourceValue is JsonObject sourceObj)
                {
                    MergeJson(targetObj, sourceObj);
                }
                else
                {
                    // Overwrite
                    target[key] = sourceValue?.DeepClone();
                }
            }
            else
            {
                target.Add(key, sourceValue?.DeepClone());
            }
        }
    }
}
