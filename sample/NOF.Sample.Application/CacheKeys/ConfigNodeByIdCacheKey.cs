using NOF.Application;

namespace NOF.Sample.Application.CacheKeys;

public record ConfigNodeByIdCacheKey(ConfigNodeId Id) : CacheKey<ConfigNodeDto>($"ConfigNode:{(long)Id}");
