using System;

namespace ZiggyCreatures.Caching.Fusion
{
	/// <summary>
	/// Configuration settings for cache eviction policies.
	/// </summary>
	public class FusionCacheEvictionPolicyConfig
	{
		/// <summary>
		/// The maximum number of entries allowed in the cache before eviction will be triggered. When <c>null</c> no capacity limit is enforced.
		/// </summary>
		public int? MaxEntryCount { get; set; }
		/// <summary>
		/// The fraction of <see cref="MaxEntryCount"/> at which eviction should start running. For example 0.8 will start evicting when the cache passes 80% of capacity.
		/// Defaults to 1.0 (evict only when capacity reached).
		/// </summary>
		public double EvictionThreshold { get; set; } = 1.0;
		/// <summary>
		/// The fraction of <see cref="MaxEntryCount"/> to evict when the eviction threshold is reached. Defaults to 0.1 (10% of capacity).
		/// </summary>
		public double EvictionPercentage { get; set; } = 0.1;
		/// <summary>
		/// The minimum number of entries to evict when eviction is triggered. Defaults to 1.
		/// </summary>
		public int MinEvictionBatchSize { get; set; } = 1;
		/// <summary>
		/// The maximum number of entries to evict when eviction is triggered. Defaults to 1000.
		/// </summary>
		public int MaxEvictionBatchSize { get; set; } = 1000;
	}
}
