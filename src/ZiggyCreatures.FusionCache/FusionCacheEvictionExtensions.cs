using System;

namespace ZiggyCreatures.Caching.Fusion
{
	/// <summary>
	/// Extension methods for configuring eviction policies on a FusionCache via the builder API.
	/// </summary>
	public static class FusionCacheEvictionExtensions
	{
		/// <summary>
		/// Configure the cache to use an <see cref="LruEvictionPolicy"/> with the specified capacity and eviction percentage.
		/// </summary>
		public static IFusionCacheBuilder WithLruEviction(this IFusionCacheBuilder builder, int maxEntryCount, double evictionPercentage = 0.1)
		{
			if (builder is null)
				throw new ArgumentNullException(nameof(builder));
			return builder.WithOptions(opts =>
			{
				opts.EvictionPolicy = new LruEvictionPolicy(new FusionCacheEvictionPolicyConfig
				{
					MaxEntryCount = maxEntryCount,
					EvictionPercentage = evictionPercentage
				});
			});
		}
		/// <summary>
		/// Configure the cache to use an <see cref="LruEvictionPolicy"/> with the specified detailed configuration.
		/// </summary>
		public static IFusionCacheBuilder WithLruEviction(this IFusionCacheBuilder builder, FusionCacheEvictionPolicyConfig config)
		{
			if (builder is null)
				throw new ArgumentNullException(nameof(builder));
			if (config is null)
				throw new ArgumentNullException(nameof(config));
			return builder.WithOptions(opts => opts.EvictionPolicy = new LruEvictionPolicy(config));
		}
		/// <summary>
		/// Configure the cache to use an <see cref="LfuEvictionPolicy"/> with the specified capacity and eviction percentage.
		/// </summary>
		public static IFusionCacheBuilder WithLfuEviction(this IFusionCacheBuilder builder, int maxEntryCount, double evictionPercentage = 0.1)
		{
			if (builder is null)
				throw new ArgumentNullException(nameof(builder));
			return builder.WithOptions(opts =>
			{
				opts.EvictionPolicy = new LfuEvictionPolicy(new FusionCacheEvictionPolicyConfig
				{
					MaxEntryCount = maxEntryCount,
					EvictionPercentage = evictionPercentage
				});
			});
		}
		/// <summary>
		/// Configure the cache to use an <see cref="LfuEvictionPolicy"/> with the specified detailed configuration.
		/// </summary>
		public static IFusionCacheBuilder WithLfuEviction(this IFusionCacheBuilder builder, FusionCacheEvictionPolicyConfig config)
		{
			if (builder is null)
				throw new ArgumentNullException(nameof(builder));
			if (config is null)
				throw new ArgumentNullException(nameof(config));
			return builder.WithOptions(opts => opts.EvictionPolicy = new LfuEvictionPolicy(config));
		}
	}
}
