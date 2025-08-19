namespace ZiggyCreatures.Caching.Fusion;

/// <summary>
/// Fluent extension methods to make configuring eviction policies on a FusionCache builder easier.
/// </summary>
public static class FusionCacheEvictionExtensions
{
	/// <summary>
	/// Configure the builder to use an LRU-based eviction policy with a fixed maximum entry count.
	/// When the cache reaches the specified count at the configured threshold, it will evict the
	/// least recently used entries.
	/// </summary>
	/// <param name="builder">The FusionCache builder.</param>
	/// <param name="maxEntryCount">Maximum number of entries allowed in the cache.</param>
	/// <param name="evictionPercentage">Percentage of entries to remove when evicting.</param>
	public static IFusionCacheBuilder WithLruEviction(this IFusionCacheBuilder builder, int maxEntryCount, double evictionPercentage = 0.1)
	{
		builder.SetupOptionsAction += options =>
		{
			options.EvictionPolicy = new LruEvictionPolicy(new FusionCacheEvictionPolicyConfig { MaxEntryCount = maxEntryCount, EvictionPercentage = evictionPercentage });
		};
		return builder;
	}
	/// <summary>
	/// Configure the builder to use an LFU-based eviction policy with a supplied configuration.
	/// </summary>
	public static IFusionCacheBuilder WithLfuEviction(this IFusionCacheBuilder builder, FusionCacheEvictionPolicyConfig config)
	{
		builder.SetupOptionsAction += options =>
		{
			options.EvictionPolicy = new LfuEvictionPolicy(config);
		};
		return builder;
	}
	// Additional helpers for size-based or custom policies can be added similarly.
}
