using System;

namespace ZiggyCreatures.Caching.Fusion;

/// <summary>
/// Extension methods for <see cref="IFusionCacheBuilder"/> to configure L1 eviction policies.
/// </summary>
public static class FusionCacheEvictionExtensions
{
	/// <summary>
	/// Configure the FusionCache to use an LRU eviction policy with the specified maximum entry count and optional eviction percentage.
	/// </summary>
	public static IFusionCacheBuilder WithLruEviction(this IFusionCacheBuilder builder, int maxEntryCount, double evictionPercentage = 0.1)
	{
		if (builder is null)
			throw new ArgumentNullException(nameof(builder));
		var cfg = new FusionCacheEvictionPolicyConfig
		{
			MaxEntryCount = maxEntryCount,
			EvictionPercentage = evictionPercentage
		};
		return builder.WithLruEviction(cfg);
	}

	/// <summary>
	/// Configure the FusionCache to use an LRU eviction policy with the specified policy configuration.
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
	/// Configure the FusionCache to use an LFU eviction policy with the specified maximum entry count and optional eviction percentage.
	/// </summary>
	public static IFusionCacheBuilder WithLfuEviction(this IFusionCacheBuilder builder, int maxEntryCount, double evictionPercentage = 0.1)
	{
		if (builder is null)
			throw new ArgumentNullException(nameof(builder));
		var cfg = new FusionCacheEvictionPolicyConfig
		{
			MaxEntryCount = maxEntryCount,
			EvictionPercentage = evictionPercentage
		};
		return builder.WithLfuEviction(cfg);
	}

	/// <summary>
	/// Configure the FusionCache to use an LFU eviction policy with the specified policy configuration.
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
