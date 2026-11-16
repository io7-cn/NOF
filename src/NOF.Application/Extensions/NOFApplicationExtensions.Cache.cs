using Microsoft.Extensions.Caching.Distributed;
using NOF.Contract;

namespace NOF.Application;

public static partial class NOFApplicationExtensions
{
    extension(ICacheService cacheService)
    {
        /// <summary>
        /// Removes a cached value by key.
        /// </summary>
        /// <typeparam name="TValue">The type of the cached value.</typeparam>
        /// <param name="key">The cache key.</param>
        public async ValueTask RemoveAsync<TValue>(CacheKey<TValue> key,
            CancellationToken cancellationToken = default)
            => await cacheService.RemoveAsync(key.Key, cancellationToken);

        /// <summary>
        /// Gets a cached value by key.
        /// </summary>
        /// <typeparam name="TValue">The type of the cached value.</typeparam>
        /// <param name="key">The cache key.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>An optional containing the cached value if found.</returns>
        public async ValueTask<Optional<TValue>> GetAsync<TValue>(CacheKey<TValue> key,
            CancellationToken cancellationToken = default)
            => await cacheService.GetAsync<TValue>(key.Key, cancellationToken);

        /// <summary>
        /// Sets a value in the cache.
        /// </summary>
        /// <typeparam name="TValue">The type of the value to cache.</typeparam>
        /// <param name="key">The cache key.</param>
        /// <param name="value">The value to cache.</param>
        /// <param name="options">Cache entry options.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        public async ValueTask SetAsync<TValue>(CacheKey<TValue> key,
            TValue value,
            DistributedCacheEntryOptions? options = null,
            CancellationToken cancellationToken = default)
            => await cacheService.SetAsync(key.Key, value, options, cancellationToken);

        /// <summary>
        /// Gets a cached value or creates and caches it using the factory if not found.
        /// </summary>
        /// <typeparam name="TValue">The type of the cached value.</typeparam>
        /// <param name="key">The cache key.</param>
        /// <param name="factory">Factory function to create the value if cache misses.</param>
        /// <param name="options">Cache entry options.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        public async ValueTask<TValue> GetOrSetAsync<TValue>(CacheKey<TValue> key,
            Func<CancellationToken, ValueTask<TValue>> factory,
            DistributedCacheEntryOptions? options = null,
            CancellationToken cancellationToken = default)
            => await cacheService.GetOrSetAsync(key.Key, factory, options, cancellationToken);

        /// <summary>
        /// Checks if a cache key exists.
        /// </summary>
        /// <typeparam name="TValue">The type of the cached value.</typeparam>
        /// <param name="key">The cache key.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        public async ValueTask<bool> ExistsAsync<TValue>(CacheKey<TValue> key,
            CancellationToken cancellationToken = default)
            => await cacheService.ExistsAsync(key.Key, cancellationToken);

        /// <summary>
        /// Gets multiple cached values by their keys.
        /// </summary>
        /// <typeparam name="TValue">The type of the cached values.</typeparam>
        /// <param name="keys">The cache keys.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        public async ValueTask<IReadOnlyDictionary<string, Optional<TValue>>> GetManyAsync<TValue>(IEnumerable<CacheKey<TValue>> keys,
            CancellationToken cancellationToken = default)
            => await cacheService.GetManyAsync<TValue>(keys.Select(k => k.Key), cancellationToken);

        /// <summary>
        /// Sets multiple values in the cache.
        /// </summary>
        /// <typeparam name="TValue">The type of the values to cache.</typeparam>
        /// <param name="items">Dictionary of keys and values to cache.</param>
        /// <param name="options">Cache entry options.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        public async ValueTask SetManyAsync<TValue>(IDictionary<CacheKey<TValue>, TValue> items,
            DistributedCacheEntryOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            var dictionary = items.ToDictionary(kvp => kvp.Key.Key, kvp => kvp.Value);
            await cacheService.SetManyAsync(dictionary, options, cancellationToken);
        }

        /// <summary>
        /// Removes multiple cached values by their keys.
        /// </summary>
        /// <typeparam name="TValue">The type of the cached values.</typeparam>
        /// <param name="keys">The cache keys to remove.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        public async ValueTask<long> RemoveManyAsync<TValue>(IEnumerable<CacheKey<TValue>> keys,
            CancellationToken cancellationToken = default)
            => await cacheService.RemoveManyAsync(keys.Select(k => k.Key), cancellationToken);

        /// <summary>
        /// Atomically increments a numeric value in the cache.
        /// </summary>
        /// <param name="key">The cache key.</param>
        /// <param name="value">The value to increment by.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        public async ValueTask<long> IncrementAsync(CacheKey<long> key,
            long value = 1,
            CancellationToken cancellationToken = default)
            => await cacheService.IncrementAsync(key.Key, value, cancellationToken);

        /// <summary>
        /// Atomically decrements a numeric value in the cache.
        /// </summary>
        /// <param name="key">The cache key.</param>
        /// <param name="value">The value to decrement by.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        public async ValueTask<long> DecrementAsync(CacheKey<long> key,
            long value = 1,
            CancellationToken cancellationToken = default)
            => await cacheService.DecrementAsync(key.Key, value, cancellationToken);

        /// <summary>
        /// Sets a value in the cache only if the key does not already exist.
        /// </summary>
        /// <typeparam name="TValue">The type of the value to cache.</typeparam>
        /// <param name="key">The cache key.</param>
        /// <param name="value">The value to cache.</param>
        /// <param name="options">Cache entry options.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        public async ValueTask<bool> SetIfNotExistsAsync<TValue>(CacheKey<TValue> key,
            TValue value,
            DistributedCacheEntryOptions? options = null,
            CancellationToken cancellationToken = default)
            => await cacheService.SetIfNotExistsAsync(key.Key, value, options, cancellationToken);

        /// <summary>
        /// Atomically gets the current value and sets a new value.
        /// </summary>
        /// <typeparam name="TValue">The type of the cached value.</typeparam>
        /// <param name="key">The cache key.</param>
        /// <param name="newValue">The new value to set.</param>
        /// <param name="options">Cache entry options.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        public async ValueTask<Optional<TValue>> GetAndSetAsync<TValue>(CacheKey<TValue> key,
            TValue newValue,
            DistributedCacheEntryOptions? options = null,
            CancellationToken cancellationToken = default)
            => await cacheService.GetAndSetAsync(key.Key, newValue, options, cancellationToken);

        /// <summary>
        /// Atomically gets and removes a value from the cache.
        /// </summary>
        /// <typeparam name="TValue">The type of the cached value.</typeparam>
        /// <param name="key">The cache key.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        public async ValueTask<Optional<TValue>> GetAndRemoveAsync<TValue>(CacheKey<TValue> key,
            CancellationToken cancellationToken = default)
            => await cacheService.GetAndRemoveAsync<TValue>(key.Key, cancellationToken);

        /// <summary>
        /// Gets the remaining time to live for a cached value.
        /// </summary>
        /// <typeparam name="TValue">The type of the cached value.</typeparam>
        /// <param name="key">The cache key.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        public async ValueTask<Optional<TimeSpan>> GetTimeToLiveAsync<TValue>(CacheKey<TValue> key,
            CancellationToken cancellationToken = default)
            => await cacheService.GetTimeToLiveAsync(key.Key, cancellationToken);

        /// <summary>
        /// Sets the time to live for a cached value.
        /// </summary>
        /// <typeparam name="TValue">The type of the cached value.</typeparam>
        /// <param name="key">The cache key.</param>
        /// <param name="expiration">The expiration time.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        public async ValueTask<bool> SetTimeToLiveAsync<TValue>(CacheKey<TValue> key,
            TimeSpan expiration,
            CancellationToken cancellationToken = default)
            => await cacheService.SetTimeToLiveAsync(key.Key, expiration, cancellationToken);

        /// <summary>
        /// Acquires a distributed lock.
        /// </summary>
        /// <typeparam name="TValue">The type associated with the lock key.</typeparam>
        /// <param name="key">The lock key.</param>
        /// <param name="expiration">The lock expiration time.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        public async ValueTask<IDistributedLock> AcquireLockAsync<TValue>(CacheKey<TValue> key,
            TimeSpan expiration,
            CancellationToken cancellationToken = default)
            => await cacheService.AcquireLockAsync(key.Key, expiration, cancellationToken);

        /// <summary>
        /// Attempts to acquire a distributed lock within the specified timeout.
        /// </summary>
        /// <typeparam name="TValue">The type associated with the lock key.</typeparam>
        /// <param name="key">The lock key.</param>
        /// <param name="expiration">The lock expiration time.</param>
        /// <param name="timeout">The maximum time to wait for the lock.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        public async ValueTask<Optional<IDistributedLock>> TryAcquireLockAsync<TValue>(CacheKey<TValue> key,
            TimeSpan expiration,
            TimeSpan timeout,
            CancellationToken cancellationToken = default)
            => await cacheService.TryAcquireLockAsync(key.Key, expiration, timeout, cancellationToken);
    }
}
