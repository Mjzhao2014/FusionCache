using System;
namespace ZiggyCreatures.Caching.Fusion;

/// <summary>
/// Extension methods on <see cref="IFusionCacheBuilder"/> to add eviction policies.
/// </summary>
public static class FusionCacheEvictionExtensions
{
	/// <summary>
	/// Configure the cache to use an LRU (least recently used) eviction policy with the specified maximum entry count and eviction percentage.
	/// When the entry count exceeds <paramref name="maxEntryCount"/> times the configured threshold percentage, an eviction pass will remove approximately
	/// <paramref name="evictionPercentage"/> of entries.
	/// </summary>
	public static IFusionCacheBuilder WithLruEviction(this IFusionCacheBuilder builder, int maxEntryCount, double evictionPercentage = 0.1)
	{
		if (builder is null)
			throw new ArgumentNullException(nameof(builder));
		var config = new FusionCacheEvictionPolicyConfig
		{
			MaxEntryCount = maxEntryCount,
			EvictionPercentage = evictionPercentage
		};
		return builder.WithLruEviction(config);
	}

	/// <summary>
	/// Configure the cache to use an LRU (least recently used) eviction policy with a custom configuration object.
	/// </summary>
	public static IFusionCacheBuilder WithLruEviction(this IFusionCacheBuilder builder, FusionCacheEvictionPolicyConfig config)
	{
		if (builder is null)
			throw new ArgumentNullException(nameof(builder));
		if (config is null)
			throw new ArgumentNullException(nameof(config));
		builder.SetupOptionsAction += options => options.EvictionPolicy = new LruEvictionPolicy(config);
		return builder;
	}

	/// <summary>
	/// Configure the cache to use an LFU (least frequently used) eviction policy with the specified maximum entry count and eviction percentage.
	/// When the entry count exceeds <paramref name="maxEntryCount"/> times the configured threshold percentage, an eviction pass will remove approximately
	/// <paramref name="evictionPercentage"/> of entries.
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
		return builder.WithLfuEviction(config);
	}

	/// <summary>
	/// Configure the cache to use an LFU (least frequently used) eviction policy with a custom configuration object.
	/// </summary>
	public static IFusionCacheBuilder WithLfuEviction(this IFusionCacheBuilder builder, FusionCacheEvictionPolicyConfig config)
	{
		if (builder is null)
			throw new ArgumentNullException(nameof(builder));
		if (config is null)
			throw new ArgumentNullException(nameof(config));
		builder.SetupOptionsAction += options => options.EvictionPolicy = new LfuEvictionPolicy(config);
		return builder;
	}
}
