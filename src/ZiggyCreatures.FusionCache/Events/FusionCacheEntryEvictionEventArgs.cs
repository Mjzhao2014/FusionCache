using Microsoft.Extensions.Caching.Memory;

namespace ZiggyCreatures.Caching.Fusion.Events;

/// <summary>
/// The specific <see cref="EventArgs"/> object for events related to cache entries' evictions.
/// </summary>
public class FusionCacheEntryEvictionEventArgs
	: FusionCacheEntryEventArgs
{
	/// <summary>
	/// Initializes a new instance of the <see cref="FusionCacheEntryEvictionEventArgs"/> class.
	/// </summary>
	/// <param name="key">The cache key related to the event.</param>
	/// <param name="reason">The reason for the eviction.</param>
	/// <param name="policyName">The optional name of the eviction policy responsible for the eviction.</param>
	/// <param name="value">The value being evicted from the cache.</param>
	public FusionCacheEntryEvictionEventArgs(string key, EvictionReason reason, string? policyName, object? value)
		: base(key)
	{
		Reason = reason;
		PolicyName = policyName;
		Value = value;
	}

	/// <summary>
	/// The reason for the eviction.
	/// </summary>
	public EvictionReason Reason { get; }

	/// <summary>
	/// The optional name of the eviction policy responsible for the eviction.
	/// Will be <see langword="null"/> if eviction was due to expiration or a manual remove.
	/// </summary>
	public string? PolicyName { get; }

	/// <summary>
	/// The value being evicted from the cache.
	/// </summary>
	public object? Value { get; }
}
