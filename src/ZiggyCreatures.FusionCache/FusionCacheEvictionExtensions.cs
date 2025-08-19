namespace ZiggyCreatures.Caching.Fusion;

/// <summary>
/// Fluent builder extensions for configuring memory eviction policies on FusionCache.
/// </summary>
public static class FusionCacheEvictionExtensions
{
	/// <summary>
	/// Configures an LRU eviction policy with an entry count limit.
	/// </summary>
	/// <param name="builder">The builder to configure.</param>
	/// <param name="maxEntryCount">Maximum number of entries before eviction triggers.</param>
	/// <param name="evictionPercentage">Fraction of entries to remove when eviction occurs.</param>
	public static IFusionCacheBuilder WithLruEviction(this IFusionCacheBuilder builder, int maxEntryCount, double evictionPercentage = 0.1d)
	{
		builder.SetupOptionsAction ??= _ => { };
		builder.SetupOptionsAction += options =>
		{
			options.EvictionPolicy = new LruEvictionPolicy(new FusionCacheEvictionPolicyConfig
			{
				MaxEntryCount = maxEntryCount,
				EvictionPercentage = evictionPercentage
			});
		};
		return builder;
	}

	/// <summary>
	/// Configures an LFU eviction policy using the provided configuration.
	/// </summary>
	public static IFusionCacheBuilder WithLfuEviction(this IFusionCacheBuilder builder, FusionCacheEvictionPolicyConfig config)
	{
		builder.SetupOptionsAction ??= _ => { };
		builder.SetupOptionsAction += options =>
		{
			options.EvictionPolicy = new LfuEvictionPolicy(config);
		};
		return builder;
	}

	/// <summary>
	/// Configures a size-based eviction policy using the provided configuration.
	/// </summary>
	public static IFusionCacheBuilder WithSizeBasedEviction(this IFusionCacheBuilder builder, FusionCacheEvictionPolicyConfig config)
	{
		builder.SetupOptionsAction ??= _ => { };
		builder.SetupOptionsAction += options =>
		{
			options.EvictionPolicy = new SizeBasedEvictionPolicy(config);
		};
		return builder;
	}
}
