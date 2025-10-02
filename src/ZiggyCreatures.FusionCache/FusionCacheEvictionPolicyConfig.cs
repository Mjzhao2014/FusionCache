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

	/// <summary>
	/// Creates a copy of this configuration instance.
	/// </summary>
	public FusionCacheEvictionPolicyConfig Duplicate()
	{
		return new FusionCacheEvictionPolicyConfig
		{
			MaxEntryCount = MaxEntryCount,
			EvictionThreshold = EvictionThreshold,
			EvictionPercentage = EvictionPercentage,
			MinEvictionBatchSize = MinEvictionBatchSize,
			MaxEvictionBatchSize = MaxEvictionBatchSize,
		};
	}

	internal void Validate()
	{
		if (MaxEntryCount.HasValue && MaxEntryCount.Value <= 0)
			throw new ArgumentOutOfRangeException(nameof(MaxEntryCount), "MaxEntryCount must be greater than zero.");

		if (EvictionThreshold <= 0 || EvictionThreshold > 1)
			throw new ArgumentOutOfRangeException(nameof(EvictionThreshold), "EvictionThreshold must be between 0 (exclusive) and 1 (inclusive).");

		if (EvictionPercentage <= 0 || EvictionPercentage > 1)
			throw new ArgumentOutOfRangeException(nameof(EvictionPercentage), "EvictionPercentage must be between 0 (exclusive) and 1 (inclusive).");

		if (MinEvictionBatchSize <= 0)
			throw new ArgumentOutOfRangeException(nameof(MinEvictionBatchSize), "MinEvictionBatchSize must be greater than zero.");

		if (MaxEvictionBatchSize <= 0)
			throw new ArgumentOutOfRangeException(nameof(MaxEvictionBatchSize), "MaxEvictionBatchSize must be greater than zero.");

		if (MaxEvictionBatchSize < MinEvictionBatchSize)
			throw new ArgumentException("MaxEvictionBatchSize must be greater than or equal to MinEvictionBatchSize.", nameof(MaxEvictionBatchSize));
	}

	internal int CalculateEvictionBatchSize(int currentCount)
	{
		if (currentCount <= 0)
			return 0;

		if (MaxEntryCount is not int capacity || capacity <= 0)
			return 0;

		bool thresholdInclusive = EvictionThreshold < 1.0;
		int triggerCount;
		if (thresholdInclusive)
		{
			triggerCount = (int)Math.Ceiling(capacity * EvictionThreshold);
			if (triggerCount <= 0)
				triggerCount = 1;
			if (currentCount < triggerCount)
				return 0;
		}
		else
		{
			triggerCount = capacity;
			if (currentCount <= triggerCount)
				return 0;
		}

		int minimumRequired = thresholdInclusive
			? Math.Max(1, currentCount - triggerCount + 1)
			: currentCount - triggerCount;

		var desired = (int)Math.Ceiling(capacity * EvictionPercentage);
		desired = Math.Max(desired, minimumRequired);
		desired = Math.Max(desired, MinEvictionBatchSize);
		desired = Math.Min(desired, MaxEvictionBatchSize);
		desired = Math.Min(desired, currentCount);

		return desired;
	}
}
