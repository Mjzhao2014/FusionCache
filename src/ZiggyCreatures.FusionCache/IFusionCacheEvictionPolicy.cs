using System.Collections.Generic;
using ZiggyCreatures.Caching.Fusion.Internals;

namespace ZiggyCreatures.Caching.Fusion
{
	/// <summary>
	/// Abstraction for a policy that maintains metadata for cache entries to enable
	/// eviction of entries when configured capacity limits are exceeded.
	/// The policy is kept in sync via notifications on get/set/remove operations,
	/// and can return a set of keys to evict when thresholds are exceeded.
	/// </summary>
	public interface IFusionCacheEvictionPolicy
	{
		/// <summary>
		/// Name of the policy (e.g. "LRU", "LFU").
		/// </summary>
		string Name { get; }
		/// <summary>
		/// The configuration associated with this policy.
		/// </summary>
		FusionCacheEvictionPolicyConfig Config { get; }
		/// <summary>
		/// Notify the policy that the given key has been accessed.
		/// </summary>
		void OnGet(string key);
		/// <summary>
		/// Notify the policy that the given key has been added or updated.
		/// The optional metadata can be used to update size tracking.
		/// </summary>
		void OnSet(string key, FusionCacheEntryMetadata? metadata);
		/// <summary>
		/// Notify the policy that the given key has been removed.
		/// </summary>
		void OnRemove(string key);
		/// <summary>
		/// Returns an ordered collection of keys that should be evicted based on
		/// current usage and thresholds.
		/// </summary>
		IEnumerable<string> GetKeysToEvict();
	}
}
