using System.Diagnostics;
using Microsoft.Extensions.Caching.Memory;
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

	// Keep a thread-local override when performing capacity evictions, to adjust reason/policyName.
	private readonly System.Threading.AsyncLocal<(EvictionReason Reason, string? PolicyName)?> _evictionOverride = new();

	internal IDisposable BeginEvictionOverride(EvictionReason reason, string? policyName)
	{
		_evictionOverride.Value = (reason, policyName);
		return new OverrideScope(this);
	}

	private void ClearEvictionOverride()
	{
		_evictionOverride.Value = null;
	}

	internal void OnEviction(string operationId, string key, EvictionReason reason, string? policyName, object? value)
	{
		var overrideVal = _evictionOverride.Value;
		if (overrideVal.HasValue)
		{
			reason = overrideVal.Value.Reason;
			policyName = overrideVal.Value.PolicyName;
			// reset after one use to avoid leaking across calls
			ClearEvictionOverride();
		}
		// METRIC
		Metrics.CounterMemoryEvict.Maybe()?.AddWithCommonTags(1, _cache.CacheName, _cache.InstanceId, new KeyValuePair<string, object?>(Tags.Names.MemoryEvictReason, reason.ToString()));

		Eviction?.SafeExecute(operationId, key, _cache, new FusionCacheEntryEvictionEventArgs(key, reason, policyName, value), nameof(Eviction), _logger, _errorsLogLevel, _syncExecution);
	}

	private sealed class OverrideScope : IDisposable
	{
		private readonly FusionCacheMemoryEventsHub _hub;
		public OverrideScope(FusionCacheMemoryEventsHub hub) => _hub = hub;
		public void Dispose() => _hub.ClearEvictionOverride();
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
