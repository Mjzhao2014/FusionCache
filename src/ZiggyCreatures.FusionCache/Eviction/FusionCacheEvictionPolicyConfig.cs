namespace ZiggyCreatures.Caching.Fusion.Eviction;

/// <summary>
/// Configuration for eviction policies including capacity limits and thresholds.
/// </summary>
public class FusionCacheEvictionPolicyConfig
{
	/// <summary>
	/// Maximum number of entries allowed in the cache. 
	/// When exceeded, entries will be evicted according to the policy.
	/// </summary>
	public int? MaxEntryCount { get; set; }

	/// <summary>
	/// Maximum total size in bytes allowed in the cache.
	/// When exceeded, entries will be evicted according to the policy.
	/// Requires entries to have size information in their metadata.
	/// </summary>
	public long? MaxTotalSize { get; set; }

	/// <summary>
	/// Percentage of max capacity at which eviction should be triggered (0.0 to 1.0).
	/// For example, 0.8 means eviction starts when cache reaches 80% capacity.
	/// Default is 1.0 (evict only when at max capacity).
	/// </summary>
	public double EvictionThreshold { get; set; } = 1.0;

	/// <summary>
	/// Percentage of entries to evict when eviction is triggered (0.0 to 1.0).
	/// For example, 0.1 means remove 10% of entries when eviction is triggered.
	/// Default is 0.1 (remove 10% of entries).
	/// </summary>
	public double EvictionPercentage { get; set; } = 0.1;

	/// <summary>
	/// Minimum number of entries to evict when eviction is triggered.
	/// Default is 1.
	/// </summary>
	public int MinEvictionCount { get; set; } = 1;

	/// <summary>
	/// Maximum number of entries to evict in a single eviction operation.
	/// Helps prevent large eviction operations that could impact performance.
	/// Default is 1000.
	/// </summary>
	public int MaxEvictionCount { get; set; } = 1000;

	/// <summary>
	/// Validates the configuration and throws if invalid.
	/// </summary>
	public void Validate()
	{
		if (MaxEntryCount is null && MaxTotalSize is null)
		{
			throw new ArgumentException("At least one of MaxEntryCount or MaxTotalSize must be specified");
		}

		if (MaxEntryCount.HasValue && MaxEntryCount.Value <= 0)
		{
			throw new ArgumentException("MaxEntryCount must be greater than 0", nameof(MaxEntryCount));
		}

		if (MaxTotalSize.HasValue && MaxTotalSize.Value <= 0)
		{
			throw new ArgumentException("MaxTotalSize must be greater than 0", nameof(MaxTotalSize));
		}

		if (EvictionThreshold <= 0 || EvictionThreshold > 1.0)
		{
			throw new ArgumentException("EvictionThreshold must be between 0 and 1.0", nameof(EvictionThreshold));
		}

		if (EvictionPercentage <= 0 || EvictionPercentage > 1.0)
		{
			throw new ArgumentException("EvictionPercentage must be between 0 and 1.0", nameof(EvictionPercentage));
		}

		if (MinEvictionCount <= 0)
		{
			throw new ArgumentException("MinEvictionCount must be greater than 0", nameof(MinEvictionCount));
		}

		if (MaxEvictionCount <= 0)
		{
			throw new ArgumentException("MaxEvictionCount must be greater than 0", nameof(MaxEvictionCount));
		}

		if (MinEvictionCount > MaxEvictionCount)
		{
			throw new ArgumentException("MinEvictionCount cannot be greater than MaxEvictionCount");
		}
	}
}