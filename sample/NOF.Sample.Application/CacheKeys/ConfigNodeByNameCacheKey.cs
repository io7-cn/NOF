using NOF.Application;

namespace NOF.Sample.Application.CacheKeys;

public record ConfigNodeByNameCacheKey(ConfigNodeName Name) : CacheKey<ConfigNodeDto>($"ConfigNode:ByName:{(string)Name}");
