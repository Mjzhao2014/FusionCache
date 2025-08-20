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
	/// <param name="value">The value being evicted from the cache.</param>
	/// <param name="policyName">If applicable, the name of the eviction policy that triggered this eviction.</param>
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
	/// The name of the eviction policy that caused the eviction, if any.
	/// If null, eviction was triggered by underlying memory cache due to expiration, etc.
	/// </summary>
	public string? PolicyName { get; }
	/// <summary>
	/// The value being evicted from the cache.
	/// </summary>
	public object? Value { get; }
}
