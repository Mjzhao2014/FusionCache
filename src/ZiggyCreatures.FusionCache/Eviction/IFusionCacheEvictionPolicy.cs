namespace ZiggyCreatures.Caching.Fusion.Eviction;

/// <summary>
/// Represents an eviction policy for L1 (memory) cache entries in FusionCache.
/// Eviction policies determine which entries should be removed when the cache
/// reaches capacity limits or when entries need to be evicted based on usage patterns.
/// </summary>
public interface IFusionCacheEvictionPolicy : IDisposable
{
	/// <summary>
	/// Gets the name of the eviction policy for identification and telemetry purposes.
	/// </summary>
	string Name { get; }

	/// <summary>
	/// Notifies the policy that an entry was accessed (read from cache).
	/// This allows policies like LRU/LFU to track access patterns.
	/// </summary>
	/// <param name="key">The cache key that was accessed</param>
	/// <param name="entry">The memory entry that was accessed</param>
	void OnEntryAccessed(string key, IFusionCacheEntryInfo entry);

	/// <summary>
	/// Notifies the policy that an entry was added or updated in the cache.
	/// </summary>
	/// <param name="key">The cache key that was set</param>
	/// <param name="entry">The memory entry that was set</param>
	void OnEntrySet(string key, IFusionCacheEntryInfo entry);

	/// <summary>
	/// Notifies the policy that an entry was removed from the cache.
	/// </summary>
	/// <param name="key">The cache key that was removed</param>
	void OnEntryRemoved(string key);

	/// <summary>
	/// Determines which entries should be evicted based on the policy's algorithm.
	/// This method should return keys ordered by eviction priority (most evictable first).
	/// </summary>
	/// <param name="currentEntries">Current entries in the cache with their metadata</param>
	/// <param name="requestedEvictionCount">Number of entries requested to be evicted</param>
	/// <returns>Collection of cache keys to evict, ordered by eviction priority</returns>
	IReadOnlyList<string> SelectEntriesForEviction(
		IReadOnlyDictionary<string, IFusionCacheEntryInfo> currentEntries,
		int requestedEvictionCount);

	/// <summary>
	/// Determines if the cache should trigger eviction based on current state.
	/// </summary>
	/// <param name="currentEntryCount">Current number of entries in cache</param>
	/// <param name="currentTotalSize">Current estimated total size in bytes (if size tracking is enabled)</param>
	/// <returns>True if eviction should be triggered</returns>
	bool ShouldTriggerEviction(int currentEntryCount, long currentTotalSize);

	/// <summary>
	/// Gets the number of entries that should be evicted when eviction is triggered.
	/// </summary>
	/// <param name="currentEntryCount">Current number of entries in cache</param>
	/// <param name="currentTotalSize">Current estimated total size in bytes</param>
	/// <returns>Number of entries to evict</returns>
	int GetEvictionCount(int currentEntryCount, long currentTotalSize);

	/// <summary>
	/// Resets the policy state. Called when the cache is cleared.
	/// </summary>
	void Reset();
}