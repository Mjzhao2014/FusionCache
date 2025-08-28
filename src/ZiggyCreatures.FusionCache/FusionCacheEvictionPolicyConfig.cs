using System;

namespace ZiggyCreatures.Caching.Fusion;

/// <summary>
/// Configuration options for an in-memory eviction policy used by FusionCache.
/// </summary>
public class FusionCacheEvictionPolicyConfig
{
    /// <summary>
    /// The maximum number of entries allowed in the in-memory cache before triggering eviction.
    /// If <see langword="null"/> no eviction will be triggered based on count.
    /// </summary>
    public int? MaxEntryCount { get; set; }

    /// <summary>
    /// The threshold fraction of <see cref="MaxEntryCount"/> at which eviction should be attempted.
    /// A value of <c>1.0</c> will only evict when the count exceeds <see cref="MaxEntryCount"/>. Lower values trigger earlier eviction.
    /// </summary>
    public double EvictionThreshold { get; set; } = 1.0;

    /// <summary>
    /// The fraction of <see cref="MaxEntryCount"/> entries to evict when an eviction is triggered.
    /// </summary>
    public double EvictionPercentage { get; set; } = 0.1;

    /// <summary>
    /// The minimum number of items to remove in a single eviction batch.
    /// </summary>
    public int MinEvictionBatchSize { get; set; } = 1;

    /// <summary>
    /// The maximum number of items to remove in a single eviction batch.
    /// </summary>
    public int MaxEvictionBatchSize { get; set; } = 1000;
}
