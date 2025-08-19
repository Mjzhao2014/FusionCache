using System.Collections.Generic;
using ZiggyCreatures.Caching.Fusion.Internals;

namespace ZiggyCreatures.Caching.Fusion;

/// <summary>
/// Abstraction for a memory-level (L1) eviction policy used by FusionCache to proactively
/// evict cache entries when capacity constraints are reached.
/// </summary>
public interface IFusionCacheEvictionPolicy
{
	/// <summary>
	/// A human-readable name for the policy (e.g. "LRU", "LFU").
	/// </summary>
	string Name { get; }

	/// <summary>
	/// The eviction policy configuration.
	/// </summary>
	FusionCacheEvictionPolicyConfig Config { get; }

	/// <summary>
	/// Should be called whenever an entry is accessed in the memory cache.
	/// Used by policies like LRU/LFU to update internal state.
	/// </summary>
	void OnGet(string key);

	/// <summary>
	/// Should be called whenever an entry is set/updated in the memory cache.
	/// </summary>
	void OnSet(string key, FusionCacheEntryMetadata? metadata);

	/// <summary>
	/// Should be called whenever an entry is removed (explicitly or via eviction) from the memory cache.
	/// </summary>
	void OnRemove(string key);

	/// <summary>
	/// When invoked, returns zero or more keys that should be evicted according to the current cache state and policy.
	/// Policies typically inspect the current state (entry count, total size etc.) and return candidates when thresholds are exceeded.
	/// </summary>
	IEnumerable<string> GetKeysToEvict();
}
