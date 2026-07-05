using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace RimAI.Framework.Execution.Cache
{
    /// <summary>
    /// Simple thread-safe in-memory cache with absolute expiration and capacity-bound eviction.
    /// Evicts the entry with the earliest expiration time when capacity is exceeded.
    /// </summary>
    public class MemoryCacheService : ICacheService
    {
        private const int DefaultMaxCapacity = 1000;

        private class CacheItem
        {
            public object Value { get; set; }
            public DateTimeOffset Expiration { get; set; }
        }

        private readonly ConcurrentDictionary<string, CacheItem> _store = new ConcurrentDictionary<string, CacheItem>(StringComparer.Ordinal);
        private readonly int _maxCapacity;

        public MemoryCacheService(int maxCapacity = DefaultMaxCapacity)
        {
            _maxCapacity = maxCapacity > 0 ? maxCapacity : DefaultMaxCapacity;
        }

        public Task<(bool hit, T value)> TryGetAsync<T>(string key, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (_store.TryGetValue(key, out var item))
            {
                if (item.Expiration > DateTimeOffset.UtcNow && item.Value is T typed)
                {
                    return Task.FromResult((true, typed));
                }
                // Expired or type mismatch -> evict
                _store.TryRemove(key, out _);
            }
            return Task.FromResult((false, default(T)));
        }

        public Task SetAsync<T>(string key, T value, TimeSpan ttl, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var item = new CacheItem
            {
                Value = value,
                Expiration = DateTimeOffset.UtcNow.Add(ttl)
            };
            _store[key] = item;

            // Evict oldest-by-expiration if over capacity (deterministic: earliest expiration goes first)
            if (_store.Count > _maxCapacity)
            {
                EvictExcess();
            }
            return Task.CompletedTask;
        }

        private void EvictExcess()
        {
            // Snapshot current entries, pick the one with earliest expiration, and try to remove it.
            // Repeat until count is within capacity or no more entries can be removed.
            while (_store.Count > _maxCapacity)
            {
                var snapshot = _store.ToArray();
                if (snapshot.Length == 0) break;

                string keyToEvict = null;
                DateTimeOffset earliest = DateTimeOffset.MaxValue;
                foreach (var kvp in snapshot)
                {
                    if (kvp.Value.Expiration < earliest)
                    {
                        earliest = kvp.Value.Expiration;
                        keyToEvict = kvp.Key;
                    }
                }

                if (keyToEvict != null)
                {
                    _store.TryRemove(keyToEvict, out _);
                }
                else
                {
                    break;
                }
            }
        }

        public Task InvalidateByPrefixAsync(string prefix, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var key in _store.Keys)
            {
                if (key.StartsWith(prefix, StringComparison.Ordinal))
                {
                    _store.TryRemove(key, out _);
                }
            }
            return Task.CompletedTask;
        }
    }
}


