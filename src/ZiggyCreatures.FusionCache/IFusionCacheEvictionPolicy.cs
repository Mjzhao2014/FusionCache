using System.Collections.Generic;
using ZiggyCreatures.Caching.Fusion.Internals;

namespace ZiggyCreatures.Caching.Fusion
{
	/// <summary>
	/// Contract for cache eviction policy implementations used to automatically evict entries from the memory cache when capacity limits are reached.
	/// All memory cache get/set/remove operations will notify the policy implementation via these hooks.
	/// </summary>
	public interface IFusionCacheEvictionPolicy
	{
		/// <summary>
		/// The name of this policy (e.g. "LRU" or "LFU").
		/// </summary>
		string Name { get; }
		/// <summary>
		/// The configuration used for this policy.
		/// </summary>
		FusionCacheEvictionPolicyConfig Config { get; }
		/// <summary>
		/// Called whenever a memory entry is successfully retrieved.
		/// </summary>
		/// <param name="key">The key that was retrieved.</param>
		void OnGet(string key);
		/// <summary>
		/// Called whenever a memory entry is set. For LFU this should be used to increment frequency.
		/// </summary>
		/// <param name="key">The key being set.</param>
		/// <param name="metadata">Optional entry metadata.</param>
		void OnSet(string key, FusionCacheEntryMetadata? metadata);
		/// <summary>
		/// Called whenever an entry is explicitly removed from the cache.
		/// </summary>
		/// <param name="key">The key being removed.</param>
		void OnRemove(string key);
		/// <summary>
		/// If the current entry count exceeds the configured threshold returns the set of keys to evict.
		/// This method should remove any returned keys from the internal policy tracking structures.
		/// </summary>
		IEnumerable<string> GetKeysToEvict();
	}
}
