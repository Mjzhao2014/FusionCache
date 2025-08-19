using System;

namespace ZiggyCreatures.Caching.Fusion;

/// <summary>
/// Configuration options used by eviction policy implementations to determine when to trigger evictions
/// and how many entries to evict.
/// </summary>
public class FusionCacheEvictionPolicyConfig
{
	/// <summary>
	/// Optional maximum number of entries allowed in the cache before evictions will be triggered.
	/// If null, no entry count limit will be considered.
	/// </summary>
	public int? MaxEntryCount { get; set; }
	/// <summary>
	/// Optional maximum total size (in user-defined units) of entries allowed in the cache before evictions will be triggered.
	/// Size information is taken from <see cref="FusionCacheEntryOptions.Size"/>.
	/// If null, no total size limit will be considered.
	/// </summary>
	public long? MaxTotalSize { get; set; }
	/// <summary>
	/// Threshold at which capacity triggers eviction. For example, 0.8 will trigger eviction when
	/// 80% of the capacity limit is reached/exceeded.
	/// Default is 1.0 (trigger at hard limit).
	/// </summary>
	public double EvictionThreshold { get; set; } = 1.0;
	/// <summary>
	/// Fraction of entries or total size to remove when evicting. For example, 0.2 will remove 20% of entries
	/// (for count-based policies) or large enough entries (for size-based) to reduce usage.
	/// </summary>
	public double EvictionPercentage { get; set; } = 0.1;
	/// <summary>
	/// Optional minimum number of items to remove in a single eviction batch.
	/// </summary>
	public int? MinEvictionBatchSize { get; set; }
	/// <summary>
	/// Optional maximum number of items to remove in a single eviction batch.
	/// </summary>
	public int? MaxEvictionBatchSize { get; set; }
}
