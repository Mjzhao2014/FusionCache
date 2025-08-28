using System;

namespace ZiggyCreatures.Caching.Fusion;

/// <summary>
/// Configuration settings for an eviction policy.
/// </summary>
public class FusionCacheEvictionPolicyConfig
{
    /// <summary>
    /// The maximum number of entries that will be allowed inside the cache.
    /// When <c>null</c> no entry-count limit will be applied.
    /// </summary>
    public int? MaxEntryCount { get; set; }

    /// <summary>
    /// The threshold, expressed as a fraction of the <see cref="MaxEntryCount"/>, at which eviction will be triggered.
    /// A value of <c>1.0</c> means eviction will be triggered when attempting to exceed the configured capacity.
    /// </summary>
    public double EvictionThreshold { get; set; } = 1.0;

    /// <summary>
    /// The percentage of the <see cref="MaxEntryCount"/> that will be removed on each eviction trigger.
    /// </summary>
    public double EvictionPercentage { get; set; } = 0.1;

    /// <summary>
    /// The minimum number of entries to evict when an eviction trigger fires.
    /// </summary>
    public int MinEvictionBatchSize { get; set; } = 1;

    /// <summary>
    /// The maximum number of entries to evict when an eviction trigger fires.
    /// </summary>
    public int MaxEvictionBatchSize { get; set; } = 1000;
}
