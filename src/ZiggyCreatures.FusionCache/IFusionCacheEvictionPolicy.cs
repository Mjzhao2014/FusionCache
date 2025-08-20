using System.Collections.Generic;
using ZiggyCreatures.Caching.Fusion.Internals;

namespace ZiggyCreatures.Caching.Fusion;

/// <summary>
/// Represents an in-memory eviction policy that can be plugged into a <see cref="IFusionCache"/>.
/// The policy implementation is responsible for tracking access and set/remove operations, and producing
/// a set of keys to remove when cache usage thresholds are exceeded.
/// Implementations must aim to keep <see cref="OnGet"/> and <see cref="OnSet"/> operations O(1).
/// </summary>
public interface IFusionCacheEvictionPolicy
{
    /// <summary>
    /// A human-readable name for the policy (e.g. "LRU" or "LFU").
    /// </summary>
    string Name { get; }

    /// <summary>
    /// The configuration being used by this policy.
    /// </summary>
    FusionCacheEvictionPolicyConfig Config { get; }

    /// <summary>
    /// Called when a cache key has been accessed.
    /// </summary>
    /// <param name="key">The cache key being accessed.</param>
    void OnGet(string key);

    /// <summary>
    /// Called when a cache key has been set/added or its value updated.
    /// </summary>
    /// <param name="key">The cache key being set.</param>
    /// <param name="metadata">Metadata for the entry being set, if available.</param>
    void OnSet(string key, FusionCacheEntryMetadata? metadata);

    /// <summary>
    /// Called when a cache key has been removed (explicitly, or due to eviction).
    /// </summary>
    /// <param name="key">The cache key being removed.</param>
    void OnRemove(string key);

    /// <summary>
    /// Check the current tracked usage against configuration thresholds and return
    /// any keys that should be removed to bring the cache back within limits.
    /// </summary>
    /// <returns>An enumerable of keys that should be evicted.</returns>
    IEnumerable<string> GetKeysToEvict();
}
