using System;

namespace ZiggyCreatures.Caching.Fusion
{
	/// <summary>
	/// Extension methods on <see cref="IFusionCacheBuilder"/> to configure memory eviction policies.
	/// </summary>
	public static class FusionCacheEvictionExtensions
	{
		/// <summary>
		/// Configure an LRU eviction policy with a simple entry count limit.
		/// </summary>
		/// <param name="builder">The builder to configure.</param>
		/// <param name="maxEntryCount">Maximum number of entries before eviction is triggered.</param>
		/// <param name="evictionPercentage">Percentage of entries to evict when triggered.</param>
		/// <returns>The builder for chaining.</returns>
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
		/// Configure an LRU eviction policy with a custom configuration object.
		/// </summary>
		public static IFusionCacheBuilder WithLruEviction(this IFusionCacheBuilder builder, FusionCacheEvictionPolicyConfig config)
		{
			if (builder is null)
				throw new ArgumentNullException(nameof(builder));
			if (config is null)
				throw new ArgumentNullException(nameof(config));
			builder.WithOptions(o => o.EvictionPolicy = new LruEvictionPolicy(config));
			return builder;
		}
		/// <summary>
		/// Configure an LFU eviction policy with a simple entry count limit.
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
		/// Configure an LFU eviction policy with a custom configuration object.
		/// </summary>
		public static IFusionCacheBuilder WithLfuEviction(this IFusionCacheBuilder builder, FusionCacheEvictionPolicyConfig config)
		{
			if (builder is null)
				throw new ArgumentNullException(nameof(builder));
			if (config is null)
				throw new ArgumentNullException(nameof(config));
			builder.WithOptions(o => o.EvictionPolicy = new LfuEvictionPolicy(config));
			return builder;
		}
	}
}
