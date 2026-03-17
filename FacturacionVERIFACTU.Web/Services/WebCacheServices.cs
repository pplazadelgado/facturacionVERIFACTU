using Microsoft.Extensions.Caching.Memory;

namespace FacturacionVERIFACTU.Web.Services;

public interface IWebCacheService
{
    Task<T?> GetOrCreateAsync<T>(string key, Func<Task<T>> factory, TimeSpan? expiration = null);
    void Invalidate(string key);
}

public class WebCacheService : IWebCacheService
{
    private readonly IMemoryCache _cache;
    private readonly ILogger<WebCacheService> _logger;

    public WebCacheService(IMemoryCache cache, ILogger<WebCacheService> logger)
    {
        _cache = cache;
        _logger = logger;
    }

    public async Task<T?> GetOrCreateAsync<T>(string key, Func<Task<T>> factory, TimeSpan? expiration = null)
    {
        if (_cache.TryGetValue(key, out T? hit))
        {
            _logger.LogDebug("[WebCache] HIT {Key}", key);
            return hit;
        }

        var value = await factory();
        _cache.Set(key, value, expiration ?? TimeSpan.FromMinutes(2));
        _logger.LogDebug("[WebCache] SET {Key}", key);
        return value;
    }

    public void Invalidate(string key)
    {
        _cache.Remove(key);
        _logger.LogDebug("[WebCache] INVALIDATED {Key}", key);
    }
}