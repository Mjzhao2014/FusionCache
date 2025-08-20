using System.Collections.Generic;
using ZiggyCreatures.Caching.Fusion.Internals;

namespace ZiggyCreatures.Caching.Fusion
{
	/// <summary>
	/// Abstraction for an in-memory cache eviction policy that can observe cache operations and decide which keys to evict
	/// when capacity constraints are exceeded.
	/// </summary>
	public interface IFusionCacheEvictionPolicy
	{
		/// <summary>
		/// A policy name for introspection/logging purposes.
		/// </summary>
		string Name { get; }
		/// <summary>
		/// The configuration driving this eviction policy.
		/// </summary>
		FusionCacheEvictionPolicyConfig Config { get; }
		/// <summary>
		/// Called after a cache entry is successfully retrieved from the in-memory cache.
		/// </summary>
		void OnGet(string key);
		/// <summary>
		/// Called after a cache entry has been set/updated in the in-memory cache.
		/// </summary>
		void OnSet(string key, FusionCacheEntryMetadata? metadata);
		/// <summary>
		/// Called when a cache entry is being removed from the in-memory cache (explicitly or implicitly via eviction).
		/// </summary>
		void OnRemove(string key);
		/// <summary>
		/// Called after a set operation to determine if any keys should be evicted due to capacity limits being exceeded.
		/// Implementations should remove the returned keys from their internal structures.
		/// </summary>
		IEnumerable<string> GetKeysToEvict();
	}
}
