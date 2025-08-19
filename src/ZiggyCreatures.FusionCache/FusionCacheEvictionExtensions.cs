namespace ZiggyCreatures.Caching.Fusion;

/// <summary>
/// Extension methods to make it easy to configure eviction policies on a <see cref="IFusionCacheBuilder"/>.
/// </summary>
public static class FusionCacheEvictionExtensions
{
	/// <summary>
	/// Configure an LRU (least recently used) eviction policy with a maximum number of entries.
	/// </summary>
	/// <param name="builder">The builder.</param>
	/// <param name="maxEntryCount">The maximum number of entries to allow in the cache before evicting.</param>
	/// <param name="evictionPercentage">The fraction of entries to evict when the threshold is exceeded (defaults to 0.1 / 10%).</param>
	/// <returns>The builder, for chaining.</returns>
	public static IFusionCacheBuilder WithLruEviction(this IFusionCacheBuilder builder, int maxEntryCount, double evictionPercentage = 0.1)
	{
		var config = new FusionCacheEvictionPolicyConfig
		{
			MaxEntryCount = maxEntryCount,
			EvictionPercentage = evictionPercentage
		};
		return builder.WithOptions(opts => opts.EvictionPolicy = new LruEvictionPolicy(config));
	}

	/// <summary>
	/// Configure an LFU (least frequently used) eviction policy with custom configuration.
	/// </summary>
	/// <param name="builder">The builder.</param>
	/// <param name="config">The eviction policy configuration to use.</param>
	/// <returns>The builder, for chaining.</returns>
	public static IFusionCacheBuilder WithLfuEviction(this IFusionCacheBuilder builder, FusionCacheEvictionPolicyConfig config)
	{
		return builder.WithOptions(opts => opts.EvictionPolicy = new LfuEvictionPolicy(config));
	}

	/// <summary>
	/// Configure a size-based eviction policy with custom configuration.
	/// </summary>
	/// <param name="builder">The builder.</param>
	/// <param name="config">The eviction policy configuration to use.</param>
	/// <returns>The builder, for chaining.</returns>
	public static IFusionCacheBuilder WithSizeBasedEviction(this IFusionCacheBuilder builder, FusionCacheEvictionPolicyConfig config)
	{
		return builder.WithOptions(opts => opts.EvictionPolicy = new SizeBasedEvictionPolicy(config));
	}
}
