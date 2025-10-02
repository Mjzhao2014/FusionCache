using System.Collections.Concurrent;
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
	// keep track of entries being removed explicitly due to capacity-based eviction so we can override the eviction reason
	private readonly ConcurrentDictionary<string, MarkedEvictionInfo> _markedEvictions = new();

	private readonly struct MarkedEvictionInfo
	{
		public MarkedEvictionInfo(string? policyName, long? timestamp)
		{
			PolicyName = policyName;
			Timestamp = timestamp;
		}

		public string? PolicyName { get; }
		public long? Timestamp { get; }
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

	/// <summary>
	/// Raises the eviction event for a cache entry that has been removed.
	/// </summary>
	/// <param name="operationId">The operation id involved.</param>
	/// <param name="key">The key being evicted.</param>
	/// <param name="reason">The reason for the eviction, as provided by the underlying memory cache.</param>
	/// <param name="policyName">If eviction was triggered by a configured eviction policy, its name; otherwise <see langword="null"/>.</param>
	/// <param name="value">The value removed from the cache.</param>
	internal void OnEviction(string operationId, string key, EvictionReason reason, string? policyName, object? value)
	{
		// METRIC
		Metrics.CounterMemoryEvict.Maybe()?.AddWithCommonTags(1, _cache.CacheName, _cache.InstanceId, new KeyValuePair<string, object?>(Tags.Names.MemoryEvictReason, reason.ToString()));

		Eviction?.SafeExecute(operationId, key, _cache, new FusionCacheEntryEvictionEventArgs(key, reason, policyName, value), nameof(Eviction), _logger, _errorsLogLevel, _syncExecution);
	}

	/// <summary>
	/// Marks a key as being removed due to capacity-based eviction, so that arguments such as policy name
	/// can be supplied to the eviction callback.
	/// </summary>
	internal void MarkEviction(string key, string? policyName, long? entryTimestamp)
	{
		_markedEvictions[key] = new MarkedEvictionInfo(policyName, entryTimestamp);
	}

	/// <summary>
	/// Attempt to retrieve and clear any marker for an eviction that was explicitly triggered by capacity.
	/// Returns true if the key had been marked.
	/// </summary>
	internal bool TryTakeMarkedEviction(string key, long? entryTimestamp, out string? policyName, out bool matched)
	{
		policyName = null;
		matched = false;

		if (_markedEvictions.TryRemove(key, out var info) == false)
		{
			return false;
		}

		policyName = info.PolicyName;
		matched = info.Timestamp is null || entryTimestamp is null || info.Timestamp.Value == entryTimestamp.Value;

		return true;
	}

	internal void ClearMarkedEviction(string key)
	{
		_markedEvictions.TryRemove(key, out _);
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
