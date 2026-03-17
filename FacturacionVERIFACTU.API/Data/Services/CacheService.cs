using Microsoft.Extensions.Caching.Memory;

namespace FacturacionVERIFACTU.API.Data.Services;

public interface ICacheService
{
    Task<T?> GetOrCreateAsync<T>(string key, Func<Task<T>> factory, TimeSpan? expiration = null);
    void Remove(string key);
    void RemoveByPrefix(string prefix);
}

public class CacheService : ICacheService
{
    private readonly IMemoryCache _cache;
    private readonly IConfiguration _config;
    private readonly ILogger<CacheService> _logger;
    private readonly HashSet<string> _keys = new();
    private readonly object _lock = new();

    public CacheService(IMemoryCache cache, IConfiguration config, ILogger<CacheService> logger)
    {
        _cache = cache;
        _config = config;
        _logger = logger;
    }

    public async Task<T?> GetOrCreateAsync<T>(string key, Func<Task<T>> factory, TimeSpan? expiration = null)
    {
        if (_cache.TryGetValue(key, out T? cached))
        {
            _logger.LogDebug("Cache HIT: {Key}", key);
            return cached;
        }

        _logger.LogDebug("Cache MISS: {Key}", key);
        var value = await factory();

        var exp = expiration ?? TimeSpan.FromMinutes(
            _config.GetValue<int>("Cache:DefaultExpirationMinutes", 5));

        var options = new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = exp,
            SlidingExpiration = TimeSpan.FromMinutes(1),
            Priority = CacheItemPriority.Normal
        };

        _cache.Set(key, value, options);

        lock (_lock) { _keys.Add(key); }

        return value;
    }

    public void Remove(string key)
    {
        _cache.Remove(key);
        lock (_lock) { _keys.Remove(key); }
        _logger.LogDebug("Cache REMOVED: {Key}", key);
    }

    public void RemoveByPrefix(string prefix)
    {
        List<string> toRemove;
        lock (_lock)
        {
            toRemove = _keys.Where(k => k.StartsWith(prefix)).ToList();
        }
        foreach (var key in toRemove)
        {
            _cache.Remove(key);
            lock (_lock) { _keys.Remove(key); }
        }
        _logger.LogDebug("Cache REMOVED {Count} keys with prefix: {Prefix}", toRemove.Count, prefix);
    }
}