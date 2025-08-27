using System;

namespace ZiggyCreatures.Caching.Fusion;

/// <summary>
/// Fluent extensions for configuring eviction policies on a FusionCache builder.
/// </summary>
public static class FusionCacheEvictionExtensions
{
	// LRU extensions
	/// <summary>
	/// Configures the cache to use a Least Recently Used eviction policy with the specified maximum entry count and optional eviction percentage.
	/// </summary>
	public static IFusionCacheBuilder WithLruEviction(this IFusionCacheBuilder builder, int maxEntryCount, double evictionPercentage = 0.1)
	{
		return builder.WithLruEviction(new FusionCacheEvictionPolicyConfig { MaxEntryCount = maxEntryCount, EvictionPercentage = evictionPercentage });
	}

	/// <summary>
	/// Configures the cache to use a Least Recently Used eviction policy using the specified configuration object.
	/// </summary>
	public static IFusionCacheBuilder WithLruEviction(this IFusionCacheBuilder builder, FusionCacheEvictionPolicyConfig config)
	{
		if (builder.Options == null)
			builder.Options = new FusionCacheOptions();
		builder.Options.EvictionPolicy = new LruEvictionPolicy(config);
		return builder;
	}

	// LFU extensions
	/// <summary>
	/// Configures the cache to use a Least Frequently Used eviction policy with the specified maximum entry count and optional eviction percentage.
	/// </summary>
	public static IFusionCacheBuilder WithLfuEviction(this IFusionCacheBuilder builder, int maxEntryCount, double evictionPercentage = 0.1)
	{
		return builder.WithLfuEviction(new FusionCacheEvictionPolicyConfig { MaxEntryCount = maxEntryCount, EvictionPercentage = evictionPercentage });
	}

	/// <summary>
	/// Configures the cache to use a Least Frequently Used eviction policy using the specified configuration object.
	/// </summary>
	public static IFusionCacheBuilder WithLfuEviction(this IFusionCacheBuilder builder, FusionCacheEvictionPolicyConfig config)
	{
		if (builder.Options == null)
			builder.Options = new FusionCacheOptions();
		builder.Options.EvictionPolicy = new LfuEvictionPolicy(config);
		return builder;
	}
}
