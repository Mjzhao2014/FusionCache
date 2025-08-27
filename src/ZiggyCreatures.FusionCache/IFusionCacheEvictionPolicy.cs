using System.Collections.Generic;
using ZiggyCreatures.Caching.Fusion.Internals;

namespace ZiggyCreatures.Caching.Fusion;

/// <summary>
/// Abstraction for an eviction policy maintained at the memory cache level (L1).
/// Implementations must support O(1) operations for get/set/remove and compute
/// the set of keys to evict when capacity thresholds are exceeded.
/// </summary>
public interface IFusionCacheEvictionPolicy
{
    /// <summary>
    /// The name of the policy, e.g. <c>LRU</c> or <c>LFU</c>.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// The configuration driving this policy instance.
    /// </summary>
    FusionCacheEvictionPolicyConfig Config { get; }

    /// <summary>
    /// Called whenever a key is read from the cache.
    /// </summary>
    /// <param name="key">The cache key being accessed.</param>
    void OnGet(string key);

    /// <summary>
    /// Called whenever a key is written into the cache.
    /// </summary>
    /// <param name="key">The cache key being written.</param>
    /// <param name="metadata">Optional cache entry metadata.</param>
    void OnSet(string key, FusionCacheEntryMetadata? metadata);

    /// <summary>
    /// Called whenever a key has been removed from the cache for any reason other than
    /// automatic capacity eviction (e.g. manual removal, expiration).
    /// </summary>
    /// <param name="key">The cache key being removed.</param>
    void OnRemove(string key);

    /// <summary>
    /// Computes the set of keys that should be evicted immediately to enforce the
    /// configured capacity constraints. Implementations should remove any returned keys
    /// from their internal bookkeeping. If no eviction is needed an empty collection is returned.
    /// </summary>
    IEnumerable<string> GetKeysToEvict();
}
