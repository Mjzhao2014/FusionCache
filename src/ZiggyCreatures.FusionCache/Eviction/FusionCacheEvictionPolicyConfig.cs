using System;

namespace ZiggyCreatures.Caching.Fusion;

/// <summary>
/// Represents the configuration for an eviction policy used to control when and how many items
/// are evicted from the in-memory FusionCache when capacity thresholds are reached.
/// </summary>
public class FusionCacheEvictionPolicyConfig
{
   /// <summary>
   /// The maximum number of entries allowed in the cache before eviction is triggered.
   /// When <see langword="null"/>, no capacity limit will be enforced.
   /// </summary>
   public int? MaxEntryCount { get; set; }

   /// <summary>
   /// What fraction of <see cref="MaxEntryCount"/> must be exceeded to trigger eviction.
   /// For example, 1.0 means eviction occurs when current count exceeds <see cref="MaxEntryCount"/>,
   /// 0.8 would trigger when current count exceeds 80% of <see cref="MaxEntryCount"/>.
   /// </summary>
   public double EvictionThreshold { get; set; } = 1.0;

   /// <summary>
   /// When eviction is triggered, what fraction of <see cref="MaxEntryCount"/> should be evicted.
   /// </summary>
   public double EvictionPercentage { get; set; } = 0.1;

   /// <summary>
   /// Minimum number of entries to evict when eviction is triggered.
   /// </summary>
   public int MinEvictionBatchSize { get; set; } = 1;

   /// <summary>
   /// Maximum number of entries to evict when eviction is triggered.
   /// </summary>
   public int MaxEvictionBatchSize { get; set; } = 1000;
}
