using System;

namespace ZiggyCreatures.Caching.Fusion
{
	/// <summary>
	/// Extensions for configuring eviction policies on an <see cref="IFusionCacheBuilder"/>.
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
			return builder.WithLruEviction(config);
		}
		public static IFusionCacheBuilder WithLruEviction(this IFusionCacheBuilder builder, FusionCacheEvictionPolicyConfig config)
		{
			if (builder is null)
				throw new ArgumentNullException(nameof(builder));
			if (config is null)
				throw new ArgumentNullException(nameof(config));
			builder.WithOptions(opt => opt.EvictionPolicy = new LruEvictionPolicy(config));
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
			return builder.WithLfuEviction(config);
		}
		public static IFusionCacheBuilder WithLfuEviction(this IFusionCacheBuilder builder, FusionCacheEvictionPolicyConfig config)
		{
			if (builder is null)
				throw new ArgumentNullException(nameof(builder));
			if (config is null)
				throw new ArgumentNullException(nameof(config));
			builder.WithOptions(opt => opt.EvictionPolicy = new LfuEvictionPolicy(config));
			return builder;
		}
	}
}
