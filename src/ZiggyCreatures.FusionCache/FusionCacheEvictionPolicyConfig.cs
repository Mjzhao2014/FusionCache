namespace ZiggyCreatures.Caching.Fusion;

/// <summary>
/// Configuration options for eviction policies used by FusionCache.
/// </summary>
public class FusionCacheEvictionPolicyConfig
{
	/// <summary>
	/// The maximum number of entries to keep in memory before eviction is triggered.
	/// If null, the number of entries is not considered when deciding to evict.
	/// </summary>
	public int? MaxEntryCount { get; set; }
	/// <summary>
	/// The maximum total size in bytes of all entries to keep in memory before eviction is triggered.
	/// If null, total size is not considered when deciding to evict.
	/// </summary>
	public long? MaxTotalSize { get; set; }
	/// <summary>
	/// When the ratio of current usage over capacity exceeds this threshold (between 0.0 and 1.0), eviction will be triggered.
	/// Defaults to 1.0 (evict only when at or above capacity).
	/// </summary>
	public double EvictionThreshold { get; set; } = 1.0;
	/// <summary>
	/// The percentage of entries to remove once eviction is triggered.
	/// Defaults to 0.1 (10%).
	/// </summary>
	public double EvictionPercentage { get; set; } = 0.1;
	/// <summary>
	/// The minimum number of entries to evict when eviction is triggered.
	/// Defaults to 1.
	/// </summary>
	public int MinEvictionBatchSize { get; set; } = 1;
	/// <summary>
	/// The maximum number of entries to evict in a single eviction pass.
	/// Defaults to 1000.
	/// </summary>
	public int MaxEvictionBatchSize { get; set; } = 1000;
}
