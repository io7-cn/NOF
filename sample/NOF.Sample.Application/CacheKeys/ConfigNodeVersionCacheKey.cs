using NOF.Application;

namespace NOF.Sample.Application.CacheKeys;

public record ConfigNodeVersionCacheKey(ConfigNodeId Id) : CacheKey<long>($"ConfigNode:Version:{(long)Id}");
