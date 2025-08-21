namespace ZiggyCreatures.Caching.Fusion;

/// <summary>
/// Represents configuration options controlling how the in-memory eviction policy behaves.
/// This can be used to set static capacity limits, as well as thresholds and batch sizes for when and how many items to evict.
/// </summary>
public class FusionCacheEvictionPolicyConfig
{
	/// <summary>
	/// The maximum number of entries allowed to be stored in the memory cache.
	/// When <c>null</c> no capacity-based eviction will take place.
	/// </summary>
	public int? MaxEntryCount { get; set; }

	/// <summary>
	/// A factor between 0.0 and 1.0 that indicates at what percentage of <see cref="MaxEntryCount"/>
	/// eviction should be triggered. For example, <c>0.8</c> would trigger eviction once the
	/// number of items reaches 80% of <see cref="MaxEntryCount"/>.
	/// Defaults to 1.0 (evict as soon as capacity is exceeded).
	/// </summary>
	public double EvictionThreshold { get; set; } = 1.0;

	/// <summary>
	/// The fraction of the <see cref="MaxEntryCount"/> to evict once the threshold is exceeded.
	/// For example, <c>0.1</c> will evict 10% of the maximum entries when triggered.
	/// Defaults to 0.1.
	/// </summary>
	public double EvictionPercentage { get; set; } = 0.1;

	/// <summary>
	/// The minimum number of entries to evict in a single batch, regardless of the
	/// <see cref="EvictionPercentage"/> calculation.
	/// Defaults to 1.
	/// </summary>
	public int MinEvictionBatchSize { get; set; } = 1;

	/// <summary>
	/// The maximum number of entries to evict in a single batch. This can be used to cap
	/// eviction work when <see cref="EvictionPercentage"/> would cause very large evictions.
	/// Defaults to 1000.
	/// </summary>
	public int MaxEvictionBatchSize { get; set; } = 1000;
}
