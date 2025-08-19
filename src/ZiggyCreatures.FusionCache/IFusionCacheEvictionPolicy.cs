using System.Collections.Generic;

namespace ZiggyCreatures.Caching.Fusion;

/// <summary>
/// Represents a policy that can be applied to the local in-memory (L1) cache to automatically
/// evict entries when capacity constraints are reached. Implementations should track accesses,
/// insertions, and removals of cache keys and decide which keys to remove when capacity
/// exceeds configured thresholds.
/// </summary>
public interface IFusionCacheEvictionPolicy
{
	/// <summary>
	/// A short name for the policy, to be used in instrumentation and events.
	/// </summary>
	string Name { get; }
	/// <summary>
	/// Called whenever a cache key is accessed (e.g. OnHit) to update internal tracking.
	/// </summary>
	/// <param name="key">The cache key that was accessed.</param>
	void OnAccess(string key);
	/// <summary>
	/// Called when a cache key is set in the cache to update internal tracking of recency/frequency
	/// and update total counts/sizes.
	/// </summary>
	/// <param name="key">The cache key that was inserted.</param>
	/// <param name="size">Optional size of the entry.</param>
	void OnSet(string key, long? size);
	/// <summary>
	/// Called when a cache key is removed from the cache (e.g. explicit remove or eviction from underlying IMemoryCache).
	/// Implementations should update internal data structures.
	/// </summary>
	/// <param name="key">The cache key being removed.</param>
	void OnRemove(string key);
	/// <summary>
	/// Examine current usage of the cache and return a sequence of keys that should be evicted to
	/// honor the configured capacity/threshold settings. Implementations can also remove the returned keys
	/// from their internal data structures in anticipation of them being removed from the cache by the caller.
	/// If no eviction is needed, return an empty set.
	/// </summary>
	IEnumerable<string> GetEvictionCandidates();
}
