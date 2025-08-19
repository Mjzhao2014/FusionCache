using ZiggyCreatures.Caching.Fusion.Internals;

namespace ZiggyCreatures.Caching.Fusion;

/// <summary>
/// Extension methods for configuring eviction policies on a FusionCache builder.
/// </summary>
public static class FusionCacheEvictionExtensions
{
	// LRU extensions
	public static IFusionCacheBuilder WithLruEviction(this IFusionCacheBuilder builder, int maxEntryCount, double evictionPercentage = 0.1)
	{
		if (builder is null)
			throw new ArgumentNullException(nameof(builder));
		var config = new FusionCacheEvictionPolicyConfig
		{
			MaxEntryCount = maxEntryCount,
			EvictionPercentage = evictionPercentage
		};
		builder.WithOptions(opts => opts.EvictionPolicy = new LruEvictionPolicy(config));
		return builder;
	}
	public static IFusionCacheBuilder WithLruEviction(this IFusionCacheBuilder builder, FusionCacheEvictionPolicyConfig config)
	{
		if (builder is null)
			throw new ArgumentNullException(nameof(builder));
		builder.WithOptions(opts => opts.EvictionPolicy = new LruEvictionPolicy(config));
		return builder;
	}

	// LFU extensions
	public static IFusionCacheBuilder WithLfuEviction(this IFusionCacheBuilder builder, int maxEntryCount, double evictionPercentage = 0.1)
	{
		if (builder is null)
			throw new ArgumentNullException(nameof(builder));
		var config = new FusionCacheEvictionPolicyConfig
		{
			MaxEntryCount = maxEntryCount,
			EvictionPercentage = evictionPercentage
		};
		builder.WithOptions(opts => opts.EvictionPolicy = new LfuEvictionPolicy(config));
		return builder;
	}
	public static IFusionCacheBuilder WithLfuEviction(this IFusionCacheBuilder builder, FusionCacheEvictionPolicyConfig config)
	{
		if (builder is null)
			throw new ArgumentNullException(nameof(builder));
		builder.WithOptions(opts => opts.EvictionPolicy = new LfuEvictionPolicy(config));
		return builder;
	}

	
}
