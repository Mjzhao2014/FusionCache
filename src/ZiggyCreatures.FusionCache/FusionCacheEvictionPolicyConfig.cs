using System;

namespace ZiggyCreatures.Caching.Fusion
{
	/// <summary>
	/// Configuration options for an eviction policy used to constrain the in-memory L1 cache.
	/// </summary>
	public class FusionCacheEvictionPolicyConfig
	{
		/// <summary>
		/// The maximum number of distinct entries to keep in the memory cache. When <see langword="null"/> no entry count limit will trigger.
		/// </summary>
		public int? MaxEntryCount { get; set; }
		/// <summary>
		/// A fractional threshold (0.0 - 1.0) of <see cref="MaxEntryCount"/> at which the eviction policy will begin to trigger.
		/// For example, a value of 0.8 means eviction will be performed once 80% of <see cref="MaxEntryCount"/> is reached.
		/// </summary>
		public double EvictionThreshold { get; set; } = 1.0;
		/// <summary>
		/// When the eviction threshold is crossed, the fraction of <see cref="MaxEntryCount"/> entries to remove during this batch.
		/// The number of entries evicted will be clamped between <see cref="MinEvictionBatchSize"/> and <see cref="MaxEvictionBatchSize"/>.
		/// </summary>
		public double EvictionPercentage { get; set; } = 0.1;
		/// <summary>
		/// The minimum number of entries to evict in a single batch.
		/// </summary>
		public int MinEvictionBatchSize { get; set; } = 1;
		/// <summary>
		/// The maximum number of entries to evict in a single batch.
		/// </summary>
		public int MaxEvictionBatchSize { get; set; } = 1000;
	}
}
