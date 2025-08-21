namespace ZiggyCreatures.Caching.Fusion;

/// <summary>
/// Fluent builder extensions for configuring capacity-based eviction policies for the in-memory cache.
/// </summary>
public static class FusionCacheEvictionExtensions
{
	/// <summary>
	/// Configure the cache to use an LRU strategy for eviction when the number of entries exceeds the given capacity.
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
	/// Configure the cache to use an LRU strategy for eviction based on the detailed configuration provided.
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
	/// Configure the cache to use an LFU strategy for eviction when the number of entries exceeds the given capacity.
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
	/// Configure the cache to use an LFU strategy for eviction based on the detailed configuration provided.
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
