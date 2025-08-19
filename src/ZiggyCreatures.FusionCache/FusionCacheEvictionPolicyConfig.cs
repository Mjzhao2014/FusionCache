using System;

namespace ZiggyCreatures.Caching.Fusion;

/// <summary>
/// Represents the set of configuration options common to eviction policies.
/// </summary>
public class FusionCacheEvictionPolicyConfig
{
	/// <summary>
	/// The optional maximum number of entries allowed in the memory cache.
	/// </summary>
	public int? MaxEntryCount { get; set; }

	/// <summary>
	/// The optional maximum total size (in bytes) allowed for entries in the memory cache.
	/// </summary>
	public long? MaxTotalSize { get; set; }

	/// <summary>
	/// The fraction of the configured capacity at which the eviction should be triggered.
	/// For example, a value of 0.8 means eviction will kick in once 80% of the capacity is reached.
	/// Defaults to 1.0 (trigger at capacity limit).
	/// </summary>
	public double EvictionThreshold { get; set; } = 1.0;

	/// <summary>
	/// The fraction of entries (or total size) to remove when an eviction is triggered.
	/// For example, a value of 0.1 means remove 10% of entries. Defaults to 0.1 (10%).
	/// </summary>
	public double EvictionPercentage { get; set; } = 0.1;

	/// <summary>
	/// Optional constraint controlling the minimum number of items to evict in a batch.
	/// </summary>
	public int? MinEvictionBatchSize { get; set; }

	/// <summary>
	/// Optional constraint controlling the maximum number of items to evict in a batch.
	/// </summary>
	public int? MaxEvictionBatchSize { get; set; }
}
