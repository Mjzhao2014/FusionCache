using Microsoft.Extensions.Caching.Memory;
using ZiggyCreatures.Caching.Fusion.Events;
using ZiggyCreatures.Caching.Fusion.Internals.Memory;

namespace ZiggyCreatures.Caching.Fusion;

/// <summary>
/// Represents a high-level eviction policy that can be plugged into FusionCache
/// to control how entries are removed from the L1 (in-memory) cache when
/// capacity limits are reached.
/// </summary>
public interface IFusionCacheEvictionPolicy
{
	/// <summary>
	/// Gets the name of this eviction policy (e.g. "LRU", "LFU", etc).
	/// </summary>
	string Name { get; }

	/// <summary>
	/// The configuration for this policy.
	/// </summary>
	FusionCacheEvictionPolicyConfig Config { get; }

	/// <summary>
	/// Called whenever an entry is added or updated in the memory cache.
	/// </summary>
	/// <param name="key">The cache key.</param>
	/// <param name="entry">The memory entry that was set.</param>
	/// <param name="size">The size of the entry being set.</param>
	void OnSet(string key, long size);

	/// <summary>
	/// Called whenever an entry is accessed in the memory cache.
	/// </summary>
	/// <param name="key">The cache key accessed.</param>
	void OnAccess(string key);

	/// <summary>
	/// Called when an entry is explicitly removed from the memory cache.
	/// </summary>
	/// <param name="key">The cache key removed.</param>
	void OnRemove(string key);

	/// <summary>
	/// Checks current usage and evicts entries according to the policy if thresholds are exceeded.
	/// Will remove entries directly from the provided memory cache and raise policy eviction events.
	/// </summary>
	/// <param name="memoryCache">The underlying memory cache.</param>
	/// <param name="events">The memory events hub to fire policy eviction events.</param>
	/// <param name="operationId">The current operation id for event correlation.</param>
	void ApplyPolicy(IMemoryCache memoryCache, FusionCacheMemoryEventsHub events, string operationId);
}
