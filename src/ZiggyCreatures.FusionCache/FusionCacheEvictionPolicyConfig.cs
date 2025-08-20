using System;

namespace ZiggyCreatures.Caching.Fusion
{
	/// <summary>
	/// Represents the configuration options for memory cache eviction policies.
	/// Both entry count and total size limits can be configured, along with
	/// trigger thresholds and batch constraints controlling how much to evict.
	/// </summary>
	public class FusionCacheEvictionPolicyConfig
	{
		/// <summary>
		/// Maximum number of entries allowed in the memory cache before eviction
		/// is triggered. When <c>null</c>, no limit is enforced on entry count.
		/// </summary>
		public int? MaxEntryCount { get; set; }
		/// <summary>
		/// Maximum aggregate size in bytes of entries allowed in the memory cache
		/// before eviction is triggered. When <c>null</c>, no size limit is enforced.
		/// </summary>
		public long? MaxTotalSize { get; set; }
		/// <summary>
		/// The fraction of configured capacity at which eviction will be triggered.
		/// For example, a value of 0.8 will trigger eviction once usage exceeds 80% of MaxEntryCount/MaxTotalSize.
		/// Defaults to 1.0 (trigger exactly at capacity).
		/// </summary>
		public double EvictionThreshold { get; set; } = 1.0;
		/// <summary>
		/// The fraction of currently used capacity to evict once triggered.
		/// For example, 0.1 will attempt to evict roughly 10% of current entries.
		/// Defaults to 0.1.
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
