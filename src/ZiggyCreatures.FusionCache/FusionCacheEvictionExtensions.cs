using System;
namespace ZiggyCreatures.Caching.Fusion;

/// <summary>
/// Helper extension methods for adding a configured eviction policy to a <see cref="IFusionCacheBuilder"/>, using fluent syntax.
/// </summary>
public static class FusionCacheEvictionExtensions
{
    /// <summary>
    /// Configures the cache to use an LRU eviction policy with the specified maximum entry count and optional eviction percentage.
    /// </summary>
    /// <param name="builder">The builder to act upon.</param>
    /// <param name="maxEntryCount">Maximum entries before evicting.</param>
    /// <param name="evictionPercentage">Percentage of entries to evict when capacity is exceeded.</param>
    /// <returns>The builder for chaining.</returns>
    public static IFusionCacheBuilder WithLruEviction(this IFusionCacheBuilder builder, int maxEntryCount, double evictionPercentage = 0.1)
    {
        if (builder is null)
            throw new ArgumentNullException(nameof(builder));
        var config = new FusionCacheEvictionPolicyConfig
        {
            MaxEntryCount = maxEntryCount,
            EvictionPercentage = evictionPercentage
        };
        builder.WithOptions(o => o.EvictionPolicy = new LruEvictionPolicy(config));
        return builder;
    }

    /// <summary>
    /// Configures the cache to use an LRU eviction policy with the specified configuration.
    /// </summary>
    public static IFusionCacheBuilder WithLruEviction(this IFusionCacheBuilder builder, FusionCacheEvictionPolicyConfig config)
    {
        if (builder is null)
            throw new ArgumentNullException(nameof(builder));
        if (config is null)
            throw new ArgumentNullException(nameof(config));
        builder.WithOptions(o => o.EvictionPolicy = new LruEvictionPolicy(config));
        return builder;
    }

    /// <summary>
    /// Configures the cache to use an LFU eviction policy with the specified maximum entry count and optional eviction percentage.
    /// </summary>
    public static IFusionCacheBuilder WithLfuEviction(this IFusionCacheBuilder builder, int maxEntryCount, double evictionPercentage = 0.1)
    {
        if (builder is null)
            throw new ArgumentNullException(nameof(builder));
        var config = new FusionCacheEvictionPolicyConfig
        {
            MaxEntryCount = maxEntryCount,
            EvictionPercentage = evictionPercentage
        };
        builder.WithOptions(o => o.EvictionPolicy = new LfuEvictionPolicy(config));
        return builder;
    }

    /// <summary>
    /// Configures the cache to use an LFU eviction policy with the specified configuration.
    /// </summary>
    public static IFusionCacheBuilder WithLfuEviction(this IFusionCacheBuilder builder, FusionCacheEvictionPolicyConfig config)
    {
        if (builder is null)
            throw new ArgumentNullException(nameof(builder));
        if (config is null)
            throw new ArgumentNullException(nameof(config));
        builder.WithOptions(o => o.EvictionPolicy = new LfuEvictionPolicy(config));
        return builder;
    }
}
