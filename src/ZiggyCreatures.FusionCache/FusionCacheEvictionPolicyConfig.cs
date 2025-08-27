using System;

namespace ZiggyCreatures.Caching.Fusion;

/// <summary>
/// Represents the configuration for a memory cache eviction policy.
/// </summary>
public class FusionCacheEvictionPolicyConfig
{
    /// <summary>
    /// A hard entry count limit. When the number of entries exceeds this limit (optionally adjusted by <see cref="EvictionThreshold"/>)
    /// an eviction will be triggered.
    /// </summary>
    public int? MaxEntryCount { get; set; }

    /// <summary>
    /// Specifies a utilization threshold, expressed as a percentage of <see cref="MaxEntryCount"/> (e.g. 0.8 = 80%).
    /// When the current entry count meets or exceeds this threshold, an eviction cycle will be triggered.
    /// Defaults to 1.0, meaning eviction only triggers at 100% of capacity.
    /// </summary>
    public double EvictionThreshold { get; set; } = 1.0;

    /// <summary>
    /// Specifies how many entries to evict when an eviction cycle is triggered, expressed as a percentage of <see cref="MaxEntryCount"/>.
    /// Defaults to 0.1, meaning 10% of the configured capacity.
    /// </summary>
    public double EvictionPercentage { get; set; } = 0.1;

    /// <summary>
    /// The minimum number of entries to evict in a single eviction pass, regardless of <see cref="EvictionPercentage"/>.
    /// Defaults to 1.
    /// </summary>
    public int MinEvictionBatchSize { get; set; } = 1;

    /// <summary>
    /// The maximum number of entries to evict in a single eviction pass, regardless of <see cref="EvictionPercentage"/>.
    /// Defaults to 1000.
    /// </summary>
    public int MaxEvictionBatchSize { get; set; } = 1000;
}
