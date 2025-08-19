using ZiggyCreatures.Caching.Fusion.Eviction;

namespace ZiggyCreatures.Caching.Fusion;

/// <summary>
/// Extension methods for configuring eviction policies on FusionCache.
/// </summary>
public static class FusionCacheEvictionExtensions
{
	/// <summary>
	/// Configures an eviction policy for the L1 (memory) cache.
	/// </summary>
	/// <param name="builder">The <see cref="IFusionCacheBuilder"/> to act upon.</param>
	/// <param name="evictionPolicy">The eviction policy to use.</param>
	/// <returns>The <see cref="IFusionCacheBuilder"/> so that additional calls can be chained.</returns>
	public static IFusionCacheBuilder WithEvictionPolicy(this IFusionCacheBuilder builder, IFusionCacheEvictionPolicy evictionPolicy)
	{
		if (builder is null)
			throw new ArgumentNullException(nameof(builder));
		if (evictionPolicy is null)
			throw new ArgumentNullException(nameof(evictionPolicy));

		builder.Options!.EvictionPolicy = evictionPolicy;
		return builder;
	}

	/// <summary>
	/// Configures a Least Recently Used (LRU) eviction policy for the L1 (memory) cache.
	/// </summary>
	/// <param name="builder">The <see cref="IFusionCacheBuilder"/> to act upon.</param>
	/// <param name="config">Configuration for the LRU eviction policy.</param>
	/// <returns>The <see cref="IFusionCacheBuilder"/> so that additional calls can be chained.</returns>
	public static IFusionCacheBuilder WithLruEviction(this IFusionCacheBuilder builder, FusionCacheEvictionPolicyConfig config)
	{
		if (builder is null)
			throw new ArgumentNullException(nameof(builder));
		if (config is null)
			throw new ArgumentNullException(nameof(config));

		return builder.WithEvictionPolicy(new LruEvictionPolicy(config));
	}

	/// <summary>
	/// Configures a Least Recently Used (LRU) eviction policy for the L1 (memory) cache.
	/// </summary>
	/// <param name="builder">The <see cref="IFusionCacheBuilder"/> to act upon.</param>
	/// <param name="maxEntryCount">Maximum number of entries before eviction is triggered.</param>
	/// <param name="evictionPercentage">Percentage of entries to evict when triggered (default: 0.1 = 10%).</param>
	/// <returns>The <see cref="IFusionCacheBuilder"/> so that additional calls can be chained.</returns>
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
	/// Configures a Least Recently Used (LRU) eviction policy for the L1 (memory) cache with size-based limits.
	/// </summary>
	/// <param name="builder">The <see cref="IFusionCacheBuilder"/> to act upon.</param>
	/// <param name="maxTotalSize">Maximum total size in bytes before eviction is triggered.</param>
	/// <param name="evictionPercentage">Percentage of entries to evict when triggered (default: 0.1 = 10%).</param>
	/// <returns>The <see cref="IFusionCacheBuilder"/> so that additional calls can be chained.</returns>
	public static IFusionCacheBuilder WithLruEvictionBySize(this IFusionCacheBuilder builder, long maxTotalSize, double evictionPercentage = 0.1)
	{
		if (builder is null)
			throw new ArgumentNullException(nameof(builder));

		var config = new FusionCacheEvictionPolicyConfig
		{
			MaxTotalSize = maxTotalSize,
			EvictionPercentage = evictionPercentage
		};

		return builder.WithLruEviction(config);
	}

	/// <summary>
	/// Configures a Least Frequently Used (LFU) eviction policy for the L1 (memory) cache.
	/// </summary>
	/// <param name="builder">The <see cref="IFusionCacheBuilder"/> to act upon.</param>
	/// <param name="config">Configuration for the LFU eviction policy.</param>
	/// <returns>The <see cref="IFusionCacheBuilder"/> so that additional calls can be chained.</returns>
	public static IFusionCacheBuilder WithLfuEviction(this IFusionCacheBuilder builder, FusionCacheEvictionPolicyConfig config)
	{
		if (builder is null)
			throw new ArgumentNullException(nameof(builder));
		if (config is null)
			throw new ArgumentNullException(nameof(config));

		return builder.WithEvictionPolicy(new LfuEvictionPolicy(config));
	}

	/// <summary>
	/// Configures a Least Frequently Used (LFU) eviction policy for the L1 (memory) cache.
	/// </summary>
	/// <param name="builder">The <see cref="IFusionCacheBuilder"/> to act upon.</param>
	/// <param name="maxEntryCount">Maximum number of entries before eviction is triggered.</param>
	/// <param name="evictionPercentage">Percentage of entries to evict when triggered (default: 0.1 = 10%).</param>
	/// <returns>The <see cref="IFusionCacheBuilder"/> so that additional calls can be chained.</returns>
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
	/// Configures a size-based eviction policy for the L1 (memory) cache that prioritizes evicting larger entries.
	/// </summary>
	/// <param name="builder">The <see cref="IFusionCacheBuilder"/> to act upon.</param>
	/// <param name="config">Configuration for the size-based eviction policy.</param>
	/// <returns>The <see cref="IFusionCacheBuilder"/> so that additional calls can be chained.</returns>
	public static IFusionCacheBuilder WithSizeBasedEviction(this IFusionCacheBuilder builder, FusionCacheEvictionPolicyConfig config)
	{
		if (builder is null)
			throw new ArgumentNullException(nameof(builder));
		if (config is null)
			throw new ArgumentNullException(nameof(config));

		return builder.WithEvictionPolicy(new SizeBasedEvictionPolicy(config));
	}

	/// <summary>
	/// Configures a size-based eviction policy for the L1 (memory) cache that prioritizes evicting larger entries.
	/// </summary>
	/// <param name="builder">The <see cref="IFusionCacheBuilder"/> to act upon.</param>
	/// <param name="maxTotalSize">Maximum total size in bytes before eviction is triggered.</param>
	/// <param name="evictionPercentage">Percentage of entries to evict when triggered (default: 0.1 = 10%).</param>
	/// <returns>The <see cref="IFusionCacheBuilder"/> so that additional calls can be chained.</returns>
	public static IFusionCacheBuilder WithSizeBasedEviction(this IFusionCacheBuilder builder, long maxTotalSize, double evictionPercentage = 0.1)
	{
		if (builder is null)
			throw new ArgumentNullException(nameof(builder));

		var config = new FusionCacheEvictionPolicyConfig
		{
			MaxTotalSize = maxTotalSize,
			EvictionPercentage = evictionPercentage
		};

		return builder.WithSizeBasedEviction(config);
	}

	/// <summary>
	/// Configures a random eviction policy for the L1 (memory) cache.
	/// </summary>
	/// <param name="builder">The <see cref="IFusionCacheBuilder"/> to act upon.</param>
	/// <param name="config">Configuration for the random eviction policy.</param>
	/// <returns>The <see cref="IFusionCacheBuilder"/> so that additional calls can be chained.</returns>
	public static IFusionCacheBuilder WithRandomEviction(this IFusionCacheBuilder builder, FusionCacheEvictionPolicyConfig config)
	{
		if (builder is null)
			throw new ArgumentNullException(nameof(builder));
		if (config is null)
			throw new ArgumentNullException(nameof(config));

		return builder.WithEvictionPolicy(new RandomEvictionPolicy(config));
	}

	/// <summary>
	/// Configures a random eviction policy for the L1 (memory) cache.
	/// </summary>
	/// <param name="builder">The <see cref="IFusionCacheBuilder"/> to act upon.</param>
	/// <param name="maxEntryCount">Maximum number of entries before eviction is triggered.</param>
	/// <param name="evictionPercentage">Percentage of entries to evict when triggered (default: 0.1 = 10%).</param>
	/// <returns>The <see cref="IFusionCacheBuilder"/> so that additional calls can be chained.</returns>
	public static IFusionCacheBuilder WithRandomEviction(this IFusionCacheBuilder builder, int maxEntryCount, double evictionPercentage = 0.1)
	{
		if (builder is null)
			throw new ArgumentNullException(nameof(builder));

		var config = new FusionCacheEvictionPolicyConfig
		{
			MaxEntryCount = maxEntryCount,
			EvictionPercentage = evictionPercentage
		};

		return builder.WithRandomEviction(config);
	}
}