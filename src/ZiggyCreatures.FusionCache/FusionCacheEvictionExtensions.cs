using System;

namespace ZiggyCreatures.Caching.Fusion;

/// <summary>
/// Extension methods to make it easy to configure eviction policies via the <see cref="IFusionCacheBuilder"/> fluently.
/// </summary>
public static class FusionCacheEvictionExtensions
{
	#region LRU
	/// <summary>
	/// Configure the cache to use an LRU eviction policy with the specified maximum entry count and optional eviction percentage.
	/// </summary>
	public static IFusionCacheBuilder WithLruEviction(this IFusionCacheBuilder builder, int maxEntryCount, double evictionPercentage = 0.1)
	{
		if (builder is null)
			throw new ArgumentNullException(nameof(builder));
		var config = new FusionCacheEvictionPolicyConfig { MaxEntryCount = maxEntryCount, EvictionPercentage = evictionPercentage };
		return builder.WithLruEviction(config);
	}

	/// <summary>
	/// Configure the cache to use an LRU eviction policy with a custom <see cref="FusionCacheEvictionPolicyConfig"/>.
	/// </summary>
	public static IFusionCacheBuilder WithLruEviction(this IFusionCacheBuilder builder, FusionCacheEvictionPolicyConfig config)
	{
		if (builder is null)
			throw new ArgumentNullException(nameof(builder));
		if (config is null)
			throw new ArgumentNullException(nameof(config));
		builder.WithOptions(options =>
		{
			options.EvictionPolicy = new LruEvictionPolicy(config);
		});
		return builder;
	}
	#endregion

	#region LFU
	/// <summary>
	/// Configure the cache to use an LFU eviction policy with the specified maximum entry count and optional eviction percentage.
	/// </summary>
	public static IFusionCacheBuilder WithLfuEviction(this IFusionCacheBuilder builder, int maxEntryCount, double evictionPercentage = 0.1)
	{
		if (builder is null)
			throw new ArgumentNullException(nameof(builder));
		var config = new FusionCacheEvictionPolicyConfig { MaxEntryCount = maxEntryCount, EvictionPercentage = evictionPercentage };
		return builder.WithLfuEviction(config);
	}
	/// <summary>
	/// Configure the cache to use an LFU eviction policy with a custom <see cref="FusionCacheEvictionPolicyConfig"/>.
	/// </summary>
	public static IFusionCacheBuilder WithLfuEviction(this IFusionCacheBuilder builder, FusionCacheEvictionPolicyConfig config)
	{
		if (builder is null)
			throw new ArgumentNullException(nameof(builder));
		if (config is null)
			throw new ArgumentNullException(nameof(config));
		builder.WithOptions(options =>
		{
			options.EvictionPolicy = new LfuEvictionPolicy(config);
		});
		return builder;
	}
	#endregion
}
