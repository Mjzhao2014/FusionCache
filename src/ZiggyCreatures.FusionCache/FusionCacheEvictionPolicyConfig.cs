using System;
namespace ZiggyCreatures.Caching.Fusion;

/// <summary>
/// Represents the common configuration settings for an in-memory eviction policy.
/// </summary>
public class FusionCacheEvictionPolicyConfig
{
	/// <summary>
	/// Gets or sets the maximum number of entries allowed before eviction will start to take place. If <see langword="null"/>, capacity-based eviction is disabled.
	/// </summary>
	public int? MaxEntryCount { get; set; }

	/// <summary>
	/// When capacity-based eviction is enabled (<see cref="MaxEntryCount"/> &gt; 0), the fraction of capacity at which eviction should be triggered.
	/// Defaults to 1.0 (evict only when over capacity).
	/// </summary>
	public double EvictionThreshold { get; set; } = 1.0;

	/// <summary>
	/// The fraction of total capacity to evict when eviction is triggered.
	/// </summary>
	public double EvictionPercentage { get; set; } = 0.1;

	/// <summary>
	/// The minimum number of entries to evict when eviction is triggered.
	/// </summary>
	public int MinEvictionBatchSize { get; set; } = 1;

	/// <summary>
	/// The maximum number of entries to evict when eviction is triggered.
	/// </summary>
	public int MaxEvictionBatchSize { get; set; } = 1000;
}
