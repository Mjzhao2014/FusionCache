using System.Collections.Generic;
using ZiggyCreatures.Caching.Fusion.Internals;

namespace ZiggyCreatures.Caching.Fusion;

/// <summary>
/// An abstraction for an in-memory (L1) eviction policy. Implementations are responsible
/// for tracking access patterns and providing a set of keys to evict when configured
/// thresholds are exceeded, while maintaining O(1) complexity for get/set/remove operations.
/// </summary>
public interface IFusionCacheEvictionPolicy
{
	/// <summary>
	/// Gets the name of this eviction policy implementation.
	/// </summary>
	string Name { get; }

	/// <summary>
	/// The configuration for this eviction policy instance.
	/// </summary>
	FusionCacheEvictionPolicyConfig Config { get; }

	/// <summary>
	/// Called whenever a cache entry has been accessed.
	/// </summary>
	void OnGet(string key);

	/// <summary>
	/// Called whenever a cache entry is being set (inserted or updated).
	/// </summary>
	void OnSet(string key, FusionCacheEntryMetadata? metadata);

	/// <summary>
	/// Called whenever a cache entry has been removed.
	/// </summary>
	void OnRemove(string key);

	/// <summary>
	/// Returns zero or more keys that should be evicted when this policy
	/// determines configured thresholds have been exceeded.
	/// This method should not alter internal state of the policy; the caller
	/// will invoke <see cref="OnRemove(string)"/> for each returned key as removal occurs.
	/// </summary>
	IEnumerable<string> GetKeysToEvict();

	/// <summary>
	/// Creates a new policy instance with the same configuration and initial state.
	/// </summary>
	IFusionCacheEvictionPolicy Duplicate();
}
