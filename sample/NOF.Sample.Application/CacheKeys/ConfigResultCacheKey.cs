using NOF.Application;

namespace NOF.Sample.Application.CacheKeys;

public record ConfigResultCacheKey(string AppName) : CacheKey<CachedConfigResult>($"ConfigResult:{AppName}");
