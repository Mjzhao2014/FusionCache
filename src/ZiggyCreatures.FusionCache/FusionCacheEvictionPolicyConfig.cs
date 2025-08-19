namespace ZiggyCreatures.Caching.Fusion;

/// <summary>
/// Configuration options for an L1 eviction policy.
/// </summary>
public class FusionCacheEvictionPolicyConfig
{
	/// <summary>
	/// Maximum number of entries allowed in the cache before eviction is triggered.
	/// If null, count-based eviction is disabled.
	/// </summary>
	public int? MaxEntryCount { get; set; }

	/// <summary>
	/// Maximum total size (sum of entry sizes) allowed in the cache before eviction is triggered.
	/// If null, size-based eviction is disabled.
	/// </summary>
	public long? MaxTotalSize { get; set; }

	/// <summary>
	/// Threshold fraction of capacity at which eviction should be triggered (e.g. 0.8 for 80%).
	/// Defaults to 0.8.
	/// </summary>
	public double EvictionThreshold { get; set; } = 0.8d;

	/// <summary>
	/// Percentage of entries (or total size) to remove when eviction occurs (e.g. 0.1 for 10%).
	/// Defaults to 0.1.
	/// </summary>
	public double EvictionPercentage { get; set; } = 0.1d;

	/// <summary>
	/// Optional minimum number of entries to remove in a single eviction pass.
	/// </summary>
	public int? MinBatchSize { get; set; }

	/// <summary>
	/// Optional maximum number of entries to remove in a single eviction pass.
	/// </summary>
	public int? MaxBatchSize { get; set; }
}
