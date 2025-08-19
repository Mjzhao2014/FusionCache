using ZiggyCreatures.Caching.Fusion.Internals;

namespace ZiggyCreatures.Caching.Fusion;

/// <summary>
/// Contract for an eviction policy to be used by FusionCache at the in-memory (L1) level.
/// An eviction policy tracks accesses and mutations to entries and can supply keys to remove when capacity thresholds are exceeded.
/// </summary>
public interface IFusionCacheEvictionPolicy
{
	/// <summary>
	/// A short human-readable name for this policy.
	/// </summary>
	string Name { get; }
	/// <summary>
	/// The configuration used by this eviction policy instance.
	/// </summary>
	FusionCacheEvictionPolicyConfig Config { get; }
	/// <summary>
	/// Called whenever the cache has successfully returned an entry for the given key.
	/// </summary>
	void OnGet(string key);
	/// <summary>
	/// Called whenever the cache has stored/updated an entry for the given key.
	/// Provides the entry metadata if available.
	/// </summary>
	void OnSet(string key, FusionCacheEntryMetadata? metadata);
	/// <summary>
	/// Called whenever an entry has been removed from the cache.
	/// This may be due to manual removal, eviction, or TTL expiration.
	/// </summary>
	void OnRemove(string key);
	/// <summary>
	/// When invoked, computes and returns the keys to evict in order to bring usage back under configured thresholds.
	/// </summary>
	IEnumerable<string> GetKeysToEvict();
}
