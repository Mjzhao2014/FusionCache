using System;

namespace ZiggyCreatures.Caching.Fusion;

/// <summary>
/// Represents configuration for an in-memory eviction policy used by <see cref="IFusionCache"/> to proactively remove
/// entries when configurable capacity thresholds are exceeded.
/// Both an entry count and an overall byte size capacity can be specified. In either case, when the current usage
/// exceeds the configured threshold percentage of the maximum capacity, an eviction pass can remove a percentage
/// of entries, constrained by optional minimum and maximum batch sizes.
/// </summary>
public class FusionCacheEvictionPolicyConfig
{
    /// <summary>
    /// The optional maximum number of cache entries allowed before eviction should be triggered.
    /// If <c>null</c>, no entry-count-based eviction is performed.
    /// </summary>
    public int? MaxEntryCount { get; set; }

    /// <summary>
    /// The optional maximum total size in bytes allowed before eviction should be triggered.
    /// If <c>null</c>, no size-based eviction is performed.
    /// </summary>
    public long? MaxTotalSize { get; set; }

    /// <summary>
    /// A fraction (0.0–1.0) of capacity at which eviction is triggered.
    /// For example, if <see cref="MaxEntryCount"/> is 1000 and this value is 0.8, eviction will be considered once the
    /// number of entries exceeds 800.
    /// Defaults to 1.0 (trigger when above the configured capacity).
    /// </summary>
    public double EvictionThreshold { get; set; } = 1.0;

    /// <summary>
    /// The fraction (0.0–1.0) of entries to remove when an eviction pass is triggered.
    /// For example, with a configured value of 0.1 and 1000 entries in the cache, an eviction pass would remove 100 entries.
    /// </summary>
    public double EvictionPercentage { get; set; } = 0.1;

    /// <summary>
    /// The minimum number of entries to remove during an eviction pass.
    /// </summary>
    public int MinEvictionBatchSize { get; set; } = 1;

    /// <summary>
    /// The maximum number of entries to remove during an eviction pass.
    /// </summary>
    public int MaxEvictionBatchSize { get; set; } = 1000;
}
