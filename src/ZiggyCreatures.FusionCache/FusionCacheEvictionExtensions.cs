using System;
using Microsoft.Extensions.DependencyInjection;
using ZiggyCreatures.Caching.Fusion.Internals.Builder;

namespace ZiggyCreatures.Caching.Fusion;

/// <summary>
/// Provides fluent extensions for configuring L1 eviction policies on an <see cref="IFusionCacheBuilder"/>.
/// </summary>
public static class FusionCacheEvictionExtensions
{
	/// <summary>
	/// Configures the cache to use an LRU eviction policy with the given capacity and eviction percentage.
	/// </summary>
	public static IFusionCacheBuilder WithLruEviction(this IFusionCacheBuilder builder, int maxEntryCount, double evictionPercentage = 0.1)
	{
		if (builder == null) throw new ArgumentNullException(nameof(builder));
		return builder.WithLruEviction(new FusionCacheEvictionPolicyConfig
		{
			MaxEntryCount = maxEntryCount,
			EvictionPercentage = evictionPercentage
		});
	}

	/// <summary>
	/// Configures the cache to use an LRU eviction policy with the specified configuration.
	/// </summary>
	public static IFusionCacheBuilder WithLruEviction(this IFusionCacheBuilder builder, FusionCacheEvictionPolicyConfig config)
	{
		if (builder == null) throw new ArgumentNullException(nameof(builder));
		if (config == null) throw new ArgumentNullException(nameof(config));
		// ensure options object exists and assign policy
		if (builder.Options == null)
		{
			builder.Options = new FusionCacheOptions();
		}
		builder.Options.EvictionPolicy = new LruEvictionPolicy(config);
		return builder;
	}

	/// <summary>
	/// Configures the cache to use an LFU eviction policy with the given capacity and eviction percentage.
	/// </summary>
	public static IFusionCacheBuilder WithLfuEviction(this IFusionCacheBuilder builder, int maxEntryCount, double evictionPercentage = 0.1)
	{
		if (builder == null) throw new ArgumentNullException(nameof(builder));
		return builder.WithLfuEviction(new FusionCacheEvictionPolicyConfig
		{
			MaxEntryCount = maxEntryCount,
			EvictionPercentage = evictionPercentage
		});
	}

	/// <summary>
	/// Configures the cache to use an LFU eviction policy with the specified configuration.
	/// </summary>
	public static IFusionCacheBuilder WithLfuEviction(this IFusionCacheBuilder builder, FusionCacheEvictionPolicyConfig config)
	{
		if (builder == null) throw new ArgumentNullException(nameof(builder));
		if (config == null) throw new ArgumentNullException(nameof(config));
		if (builder.Options == null)
		{
			builder.Options = new FusionCacheOptions();
		}
		builder.Options.EvictionPolicy = new LfuEvictionPolicy(config);
		return builder;
	}
}
