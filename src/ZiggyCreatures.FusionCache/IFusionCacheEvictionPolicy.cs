using System.Collections.Generic;
using ZiggyCreatures.Caching.Fusion.Internals;

namespace ZiggyCreatures.Caching.Fusion;

/// <summary>
/// Represents a strategy for tracking in-memory cache entries and deciding when
/// and which entries should be evicted when capacity limits are reached.
/// Implementations must provide O(1) get/set/remove operations for tracking.
/// </summary>
public interface IFusionCacheEvictionPolicy
{
	/// <summary>
	/// A human-readable name of the eviction policy that will be included in eviction events.
	/// </summary>
	string Name { get; }

	/// <summary>
	/// The configuration controlling capacity thresholds and eviction batch sizes for this policy.
	/// </summary>
	FusionCacheEvictionPolicyConfig Config { get; }

	/// <summary>
	/// Called by the cache when a memory entry is read.
	/// Implementations should update their tracking structures to mark this key as recently used.
	/// </summary>
	/// <param name="key">The cache key that was read.</param>
	void OnGet(string key);

	/// <summary>
	/// Called by the cache when a memory entry is inserted or updated.
	/// Implementations should update their tracking structures to mark this key as recently used
	/// (and increment frequency for LFU, if appropriate).
	/// </summary>
	/// <param name="key">The cache key being set.</param>
	/// <param name="metadata">Associated entry metadata, if available.</param>
	void OnSet(string key, FusionCacheEntryMetadata? metadata);

	/// <summary>
	/// Called by the cache when a memory entry is removed from the underlying store for any reason (e.g. manual remove, TTL expiration, capacity eviction).
	/// Implementations must update their tracking structures to remove the key.
	/// </summary>
	/// <param name="key">The cache key that was removed.</param>
	void OnRemove(string key);

	/// <summary>
	/// Returns a collection of keys that should be evicted based on current counts and thresholds.
	/// Implementations should not modify their own tracking state; the cache will call <see cref="OnRemove"/>
	/// for each evicted key.
	/// </summary>
	IEnumerable<string> GetKeysToEvict();
}
