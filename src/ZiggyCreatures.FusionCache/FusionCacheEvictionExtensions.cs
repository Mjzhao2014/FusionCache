using System;

namespace ZiggyCreatures.Caching.Fusion;

/// <summary>
/// Fluent API extensions to configure in-memory eviction policies like LRU/LFU on the FusionCache builder.
/// </summary>
public static class FusionCacheEvictionExtensions
{
	/// <summary>
	/// Configure an LRU eviction policy using an entry count limit.
	/// </summary>
	public static IFusionCacheBuilder WithLruEviction(this IFusionCacheBuilder builder, int maxEntryCount, double evictionPercentage = 0.1)
	{
		if (builder is null)
			throw new ArgumentNullException(nameof(builder));
		return builder.WithLruEviction(new FusionCacheEvictionPolicyConfig
		{
			MaxEntryCount = maxEntryCount,
			EvictionPercentage = evictionPercentage
		});
	}

	/// <summary>
	/// Configure an LRU eviction policy using a full config.
	/// </summary>
	public static IFusionCacheBuilder WithLruEviction(this IFusionCacheBuilder builder, FusionCacheEvictionPolicyConfig config)
	{
		if (builder is null)
			throw new ArgumentNullException(nameof(builder));
		if (config is null)
			throw new ArgumentNullException(nameof(config));
		return builder.WithOptions(opts => opts.EvictionPolicy = new LruEvictionPolicy(config));
	}

	/// <summary>
	/// Configure an LFU eviction policy using an entry count limit.
	/// </summary>
	public static IFusionCacheBuilder WithLfuEviction(this IFusionCacheBuilder builder, int maxEntryCount, double evictionPercentage = 0.1)
	{
		if (builder is null)
			throw new ArgumentNullException(nameof(builder));
		return builder.WithLfuEviction(new FusionCacheEvictionPolicyConfig
		{
			MaxEntryCount = maxEntryCount,
			EvictionPercentage = evictionPercentage
		});
	}

	/// <summary>
	/// Configure an LFU eviction policy using a full config.
	/// </summary>
	public static IFusionCacheBuilder WithLfuEviction(this IFusionCacheBuilder builder, FusionCacheEvictionPolicyConfig config)
	{
		if (builder is null)
			throw new ArgumentNullException(nameof(builder));
		if (config is null)
			throw new ArgumentNullException(nameof(config));
		return builder.WithOptions(opts => opts.EvictionPolicy = new LfuEvictionPolicy(config));
	}
}
