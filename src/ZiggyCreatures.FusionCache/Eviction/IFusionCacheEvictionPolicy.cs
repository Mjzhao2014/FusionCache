using System.Collections.Generic;
using ZiggyCreatures.Caching.Fusion.Internals;

namespace ZiggyCreatures.Caching.Fusion;

/// <summary>
/// Contract for a L1 in-memory eviction policy implementation (e.g. LRU, LFU).
/// The policy receives notifications as items in the memory cache are
/// accessed, added and removed, and can produce a list of keys that should
/// be evicted when capacity thresholds are exceeded.
/// </summary>
public interface IFusionCacheEvictionPolicy
{
   /// <summary>
   /// The name of the policy, e.g. "LRU" or "LFU".
   /// </summary>
   string Name { get; }

   /// <summary>
   /// The configuration governing capacity limits and eviction batch sizes.
   /// </summary>
   FusionCacheEvictionPolicyConfig Config { get; }

   /// <summary>
   /// Notify the policy that a cache entry was successfully read.
   /// </summary>
   /// <param name="key">The cache key being accessed.</param>
   void OnGet(string key);

   /// <summary>
   /// Notify the policy that a cache entry has been set (inserted/updated).
   /// </summary>
   /// <param name="key">The cache key being set.</param>
   /// <param name="metadata">The metadata associated with the entry, if any.</param>
   void OnSet(string key, FusionCacheEntryMetadata? metadata);

   /// <summary>
   /// Notify the policy that a cache entry has been removed.
   /// </summary>
   /// <param name="key">The cache key being removed.</param>
   void OnRemove(string key);

   /// <summary>
   /// When the current number of entries exceeds capacity threshold, produce the set of keys
   /// that should be evicted according to the policy.
   /// </summary>
   IEnumerable<string> GetKeysToEvict();
}
