using System;

namespace ZiggyCreatures.Caching.Fusion;

/// <summary>
/// Configuration options for a cache eviction policy.
/// </summary>
public class FusionCacheEvictionPolicyConfig
{
    /// <summary>
    /// The maximum number of entries allowed before evictions should start happening.
    /// When <see langword="null"/> no maximum entry count will be applied.
    /// </summary>
    public int? MaxEntryCount { get; set; }

    /// <summary>
    /// The threshold expressed as a fraction of the <see cref="MaxEntryCount"/> at which
    /// eviction should be triggered. A value of <c>1.0</c> means to trigger at 100% capacity.
    /// </summary>
    public double EvictionThreshold { get; set; } = 1.0d;

    /// <summary>
    /// The percentage of <see cref="MaxEntryCount"/> to evict when an eviction is triggered.
    /// Defaults to removing 10% of entries.
    /// </summary>
    public double EvictionPercentage { get; set; } = 0.1d;

    /// <summary>
    /// The minimum number of entries to evict in a single batch.
    /// </summary>
    public int MinEvictionBatchSize { get; set; } = 1;

    /// <summary>
    /// The maximum number of entries to evict in a single batch.
    /// </summary>
    public int MaxEvictionBatchSize { get; set; } = 1000;
}
