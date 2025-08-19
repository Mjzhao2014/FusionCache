namespace ZiggyCreatures.Caching.Fusion.Eviction;

/// <summary>
/// Public interface that provides read-only access to cache entry information
/// for eviction policy implementations.
/// </summary>
public interface IFusionCacheEntryInfo
{
	/// <summary>
	/// Gets the timestamp when the entry was created or last updated.
	/// </summary>
	long Timestamp { get; }

	/// <summary>
	/// Gets the logical expiration timestamp of the entry.
	/// </summary>
	long LogicalExpirationTimestamp { get; }

	/// <summary>
	/// Gets the tags associated with the entry.
	/// </summary>
	string[]? Tags { get; }

	/// <summary>
	/// Gets whether the entry is logically expired.
	/// </summary>
	/// <returns>True if the entry is logically expired</returns>
	bool IsLogicallyExpired();

	/// <summary>
	/// Gets the estimated size of the entry in bytes, if available.
	/// </summary>
	/// <returns>Size in bytes if available, null otherwise</returns>
	long? GetSize();

	/// <summary>
	/// Gets the priority of the entry for caching purposes.
	/// </summary>
	/// <returns>The cache item priority if available, null otherwise</returns>
	byte? GetPriority();
}