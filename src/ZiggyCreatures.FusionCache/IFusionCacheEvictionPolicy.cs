using System.Collections.Generic;
using ZiggyCreatures.Caching.Fusion.Internals;

namespace ZiggyCreatures.Caching.Fusion;

/// <summary>
/// Represents a strategy for selecting entries to evict from the in-memory cache when capacity limits are reached.
/// Implementations should ensure that <see cref="OnGet"/>, <see cref="OnSet"/> and <see cref="OnRemove"/> are all O(1).
/// </summary>
public interface IFusionCacheEvictionPolicy
{
    /// <summary>
    /// The friendly name for this policy, e.g. "LRU" or "LFU".
    /// </summary>
    string Name { get; }

    /// <summary>
    /// The configuration driving eviction thresholds and batch sizes.
    /// </summary>
    FusionCacheEvictionPolicyConfig Config { get; }

    /// <summary>
    /// Notifies the policy that a cache entry with the specified key has been accessed.
    /// Called on every cache hit.
    /// </summary>
    void OnGet(string key);

    /// <summary>
    /// Notifies the policy that a cache entry with the specified key has been written or updated.
    /// Called after a successful set.
    /// </summary>
    /// <param name="key">The cache key.</param>
    /// <param name="metadata">Metadata for the entry (size, priority).</param>
    void OnSet(string key, FusionCacheEntryMetadata? metadata);

    /// <summary>
    /// Notifies the policy that a cache entry with the specified key has been removed from the cache.
    /// Called on explicit removals as well as eviction-driven removals.
    /// </summary>
    void OnRemove(string key);

    /// <summary>
    /// When capacity limits are exceeded, returns an ordered collection of keys that should be evicted
    /// to bring the cache back under the threshold. The policy should not mutate internal state here;
    /// <see cref="OnRemove"/> will be called individually as entries are removed.
    /// </summary>
    IEnumerable<string> GetKeysToEvict();
}
