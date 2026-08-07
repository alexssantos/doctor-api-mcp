using System.Collections.Concurrent;
using McpApis.McpServer.Infrastructure.Telemetry;

namespace McpApis.McpServer.Infrastructure.Caching;

public interface IObservabilityCache
{
    Task<T> GetOrCreateAsync<T>(
        string key,
        TimeSpan ttl,
        Func<CancellationToken, Task<T>> factory,
        CancellationToken cancellationToken = default);

    void Invalidate(string keyPrefix);
}

public sealed class ObservabilityCache : IObservabilityCache
{
    private sealed record CacheEntry(object Value, DateTimeOffset ExpiresAt);

    private readonly ConcurrentDictionary<string, CacheEntry> _entries = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, Lazy<Task<object>>> _inflight = new(StringComparer.Ordinal);

    public async Task<T> GetOrCreateAsync<T>(
        string key,
        TimeSpan ttl,
        Func<CancellationToken, Task<T>> factory,
        CancellationToken cancellationToken = default)
    {
        if (_entries.TryGetValue(key, out var cached) && cached.ExpiresAt > DateTimeOffset.UtcNow)
        {
            ObservabilityTelemetry.CacheRequests.Add(1, new KeyValuePair<string, object?>("cache.result", "hit"));
            return (T)cached.Value!;
        }

        ObservabilityTelemetry.CacheRequests.Add(1, new KeyValuePair<string, object?>("cache.result", "miss"));
        var lazy = _inflight.GetOrAdd(key, _ => new Lazy<Task<object>>(
            async () => (object)(await factory(cancellationToken))!,
            LazyThreadSafetyMode.ExecutionAndPublication));

        try
        {
            var value = (T)await lazy.Value.WaitAsync(cancellationToken);
            _entries[key] = new CacheEntry(value!, DateTimeOffset.UtcNow + ttl);
            return value;
        }
        finally
        {
            _inflight.TryRemove(new KeyValuePair<string, Lazy<Task<object>>>(key, lazy));
        }
    }

    public void Invalidate(string keyPrefix)
    {
        foreach (var key in _entries.Keys.Where(k => k.StartsWith(keyPrefix, StringComparison.Ordinal)))
            _entries.TryRemove(key, out _);
    }
}
