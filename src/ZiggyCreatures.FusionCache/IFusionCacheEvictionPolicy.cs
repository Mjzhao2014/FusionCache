using System.Collections.Generic;
using ZiggyCreatures.Caching.Fusion.Internals;

namespace ZiggyCreatures.Caching.Fusion;

/// <summary>
/// Contract for an eviction policy that tracks usage of items in the L1/memory cache
/// and determines which items should be removed when capacity limits are reached.
/// </summary>
public interface IFusionCacheEvictionPolicy
{
    /// <summary>
    /// A human-readable name for this policy (e.g. "LRU" or "LFU").
    /// </summary>
    string Name { get; }

    /// <summary>
    /// The configuration controlling thresholds and batch sizes for eviction.
    /// </summary>
    FusionCacheEvictionPolicyConfig Config { get; }

    /// <summary>
    /// Called by the cache when an entry is accessed.
    /// </summary>
    /// <param name="key">The cache key.</param>
    void OnGet(string key);

    /// <summary>
    /// Called by the cache when an entry is inserted or updated.
    /// </summary>
    /// <param name="key">The cache key.</param>
    /// <param name="metadata">Optional metadata for the entry.</param>
	void OnSet(string key, FusionCacheEntryMetadata? metadata);

    /// <summary>
    /// Called by the cache when an entry is removed explicitly.
    /// </summary>
    /// <param name="key">The cache key to remove from any tracking structures.</param>
    void OnRemove(string key);

    /// <summary>
    /// Inspect the current usage and determine whether eviction should occur.
    /// If so, remove and return the keys of entries to evict from the policy's internal structures.
    /// </summary>
    /// <returns>The keys to evict. If no eviction is necessary, returns an empty enumerable.</returns>
    IEnumerable<string> GetKeysToEvict();
}
