using System.Collections.Generic;

namespace ZiggyCreatures.Caching.Fusion;

/// <summary>
/// Abstraction for an in-memory eviction policy used to automatically evict items from the MemoryCache
/// when capacity limits have been reached.
/// Implementations must maintain O(1) operations for get/set/remove tracking.
/// </summary>
public interface IFusionCacheEvictionPolicy
{
    /// <summary>
    /// A short name for this policy (e.g. "LRU" or "LFU").
    /// </summary>
    string Name { get; }

    /// <summary>
    /// The configuration for this policy.
    /// </summary>
    FusionCacheEvictionPolicyConfig Config { get; }

    /// <summary>
    /// Informs the policy that the given key has just been read.
    /// </summary>
    /// <param name="key">The cache key.</param>
    void OnGet(string key);

    /// <summary>
    /// Informs the policy that the given key has just been set.
    /// The provided metadata is the metadata stored on the cache entry.
    /// </summary>
    /// <param name="key">The cache key.</param>
    /// <param name="metadata">The metadata attached to the entry (may be null).</param>
	void OnSet(string key, Internals.FusionCacheEntryMetadata? metadata);

    /// <summary>
    /// Informs the policy that the given key has just been removed from the cache.
    /// </summary>
    /// <param name="key">The cache key.</param>
    void OnRemove(string key);

    /// <summary>
    /// Returns zero or more keys that should be evicted in order to satisfy capacity constraints.
    /// Implementations should not modify their internal state when returning keys; state should be updated via <see cref="OnRemove"/>.
    /// </summary>
    IEnumerable<string> GetKeysToEvict();
}
