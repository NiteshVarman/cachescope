using System.Text.Json;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace CacheScope.RedisCache;

public sealed class RedisCacheLayer(IConnectionMultiplexer mux, IOptions<RedisCacheOptions> options)
    : IRedisCacheLayer
{
    private readonly RedisCacheOptions _options = options.Value;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public bool IsConnected => mux.IsConnected;

    private string Prefixed(string key) => _options.KeyPrefix + key;

    public async Task<T?> GetAsync<T>(string key, CancellationToken ct = default)
    {
        var value = await mux.GetDatabase().StringGetAsync(Prefixed(key));
        if (value.IsNullOrEmpty)
        {
            return default;
        }

        return JsonSerializer.Deserialize<T>((string)value!, Json);
    }

    public async Task SetAsync<T>(string key, T value, TimeSpan? ttl = null, CancellationToken ct = default)
    {
        var payload = JsonSerializer.Serialize(value, Json);
        await mux.GetDatabase().StringSetAsync(Prefixed(key), payload, ttl ?? _options.DefaultTtl);
    }

    public Task RemoveAsync(string key, CancellationToken ct = default) =>
        mux.GetDatabase().KeyDeleteAsync(Prefixed(key));

    public Task<bool> ExpireNowAsync(string key, CancellationToken ct = default) =>
        mux.GetDatabase().KeyDeleteAsync(Prefixed(key));

    public async Task FlushAsync(CancellationToken ct = default)
    {
        // Delete only CacheScope-namespaced keys, not the whole Redis instance.
        foreach (var endpoint in mux.GetEndPoints())
        {
            var server = mux.GetServer(endpoint);
            if (!server.IsConnected || server.IsReplica)
            {
                continue;
            }

            var keys = server.Keys(pattern: _options.KeyPrefix + "*").ToArray();
            if (keys.Length > 0)
            {
                await mux.GetDatabase().KeyDeleteAsync(keys);
            }
        }
    }
}
