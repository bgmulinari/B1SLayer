using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace B1SLayer;

internal class SimpleMemoryDistributedCache : IDistributedCache
{
    private readonly MemoryCache _memCache = new(new MemoryCacheOptions());

    public byte[] Get(string key)
    {
        if (key == null)
            throw new ArgumentNullException(nameof(key));

        _memCache.TryGetValue(key, out byte[] value);
        return value;
    }

    public Task<byte[]> GetAsync(string key, CancellationToken token = default)
    {
        if (key == null)
            throw new ArgumentNullException(nameof(key));

        return Task.FromResult(Get(key));
    }

    public void Set(string key, byte[] value, DistributedCacheEntryOptions options)
    {
        if (key == null)
            throw new ArgumentNullException(nameof(key));
        if (value == null)
            throw new ArgumentNullException(nameof(value));
        if (options == null)
            throw new ArgumentNullException(nameof(options));

        var memoryCacheEntryOptions = new MemoryCacheEntryOptions
        {
            AbsoluteExpiration = options.AbsoluteExpiration,
            AbsoluteExpirationRelativeToNow = options.AbsoluteExpirationRelativeToNow,
            SlidingExpiration = options.SlidingExpiration,
            Size = value.Length
        };
        _memCache.Set(key, value, memoryCacheEntryOptions);
    }

    public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options,
        CancellationToken token = default)
    {
        if (key == null)
            throw new ArgumentNullException(nameof(key));
        if (value == null)
            throw new ArgumentNullException(nameof(value));
        if (options == null)
            throw new ArgumentNullException(nameof(options));

        Set(key, value, options);
        return Task.CompletedTask;
    }

    public void Refresh(string key)
    {
        if (key == null)
            throw new ArgumentNullException(nameof(key));

        _memCache.TryGetValue(key, out _);
    }

    public Task RefreshAsync(string key, CancellationToken token = default)
    {
        if (key == null)
            throw new ArgumentNullException(nameof(key));

        Refresh(key);
        return Task.CompletedTask;
    }

    public void Remove(string key)
    {
        if (key == null)
            throw new ArgumentNullException(nameof(key));

        _memCache.Remove(key);
    }

    public Task RemoveAsync(string key, CancellationToken token = default)
    {
        if (key == null)
            throw new ArgumentNullException(nameof(key));

        Remove(key);
        return Task.CompletedTask;
    }
}