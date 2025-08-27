using System.Diagnostics;
using Microsoft.Extensions.Caching.Memory;
using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using ZiggyCreatures.Caching.Fusion.Internals;
using ZiggyCreatures.Caching.Fusion.Internals.Diagnostics;

namespace ZiggyCreatures.Caching.Fusion.Events;

	/// <summary>
	/// The events hub for events specific for the memory level.
	/// </summary>
	public sealed class FusionCacheMemoryEventsHub
		: FusionCacheCommonEventsHub
	{
		private readonly ConcurrentDictionary<string, string> _suppressedEvictionKeys = new();

		/// <summary>
		/// Internal accessor to the currently configured eviction policy, if any.
		/// </summary>
		internal IFusionCacheEvictionPolicy? CurrentEvictionPolicy
		{
			get { return _options.EvictionPolicy; }
		}
	/// <summary>
	/// Initializes a new instance of the <see cref="FusionCacheMemoryEventsHub" /> class.
	/// </summary>
	/// <param name="cache">The <see cref="IFusionCache" /> instance.</param>
	/// <param name="options">The <see cref="FusionCacheOptions" /> instance.</param>
	/// <param name="logger">The <see cref="ILogger" /> instance.</param>
	public FusionCacheMemoryEventsHub(IFusionCache cache, FusionCacheOptions options, ILogger? logger)
		: base(cache, options, logger)
	{
	}

	/// <summary>
	/// The event for a cache eviction.
	/// </summary>
	public event EventHandler<FusionCacheEntryEvictionEventArgs>? Eviction;

	/// <summary>
	/// The event for a manual cache Expire() call.
	/// </summary>
	public event EventHandler<FusionCacheEntryEventArgs>? Expire;

	/// <summary>
	/// Check if the <see cref="Eviction"/> event has subscribers or not.
	/// </summary>
	/// <returns><see langword="true"/> if the <see cref="Eviction"/> event has subscribers, otherwise <see langword="false"/>.</returns>
	public bool HasEvictionSubscribers()
	{
		return Eviction is not null;
	}

	internal void OnEviction(string operationId, string key, EvictionReason reason, object? value)
	{
		OnEviction(operationId, key, reason, null, value);
	}

	internal void OnEviction(string operationId, string key, EvictionReason reason, string? policyName, object? value)
	{
		// METRIC
		Metrics.CounterMemoryEvict.Maybe()?.AddWithCommonTags(1, _cache.CacheName, _cache.InstanceId, new KeyValuePair<string, object?>(Tags.Names.MemoryEvictReason, reason.ToString()));

		Eviction?.SafeExecute(operationId, key, _cache, new FusionCacheEntryEvictionEventArgs(key, reason, policyName ?? string.Empty, value), nameof(Eviction), _logger, _errorsLogLevel, _syncExecution);
	}

	/// <summary>
	/// Marks an eviction event for the specified key as originating from a capacity-driven policy eviction,
	/// so that the normal PostEviction callback can override the reason and policy name when raising the event.
	/// </summary>
	/// <param name="key">The cache key to suppress the default eviction reason for.</param>
	/// <param name="policyName">The policy name to associate with the eviction.</param>
	internal void SuppressEvictionNotification(string key, string policyName)
	{
		_suppressedEvictionKeys[key] = policyName;
	}

	/// <summary>
	/// Try to remove a key that was previously suppressed for eviction notifications,
	/// returning the policy name if present.
	/// </summary>
	internal bool TryRetrieveSuppressedEviction(string key, out string policyName)
	{
		return _suppressedEvictionKeys.TryRemove(key, out policyName);
	}

	internal void OnExpire(string operationId, string key)
	{
		// METRIC
		Metrics.CounterMemoryExpire.Maybe()?.AddWithCommonTags(1, _cache.CacheName, _cache.InstanceId);

		Expire?.SafeExecute(operationId, key, _cache, new FusionCacheEntryEventArgs(key), nameof(Expire), _logger, _errorsLogLevel, _syncExecution);
	}

	internal override void OnHit(string operationId, string key, bool isStale, Activity? activity)
	{
		// METRIC
		Metrics.CounterMemoryHit.Maybe()?.AddWithCommonTags(1, _cache.CacheName, _cache.InstanceId, new KeyValuePair<string, object?>(Tags.Names.Stale, isStale));

		base.OnHit(operationId, key, isStale, activity);
	}

	internal override void OnMiss(string operationId, string key, Activity? activity)
	{
		// METRIC
		Metrics.CounterMemoryMiss.Maybe()?.AddWithCommonTags(1, _cache.CacheName, _cache.InstanceId);

		base.OnMiss(operationId, key, activity);
	}

	internal override void OnSet(string operationId, string key)
	{
		// METRIC
		Metrics.CounterMemorySet.Maybe()?.AddWithCommonTags(1, _cache.CacheName, _cache.InstanceId);

		base.OnSet(operationId, key);
	}

	internal override void OnRemove(string operationId, string key)
	{
		// METRIC
		Metrics.CounterMemoryRemove.Maybe()?.AddWithCommonTags(1, _cache.CacheName, _cache.InstanceId);

		base.OnRemove(operationId, key);
	}
}
