using System.Diagnostics;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using ZiggyCreatures.Caching.Fusion.Events;
using ZiggyCreatures.Caching.Fusion.Internals.Diagnostics;
using System.Threading;
using ZiggyCreatures.Caching.Fusion.Internals.Distributed;

namespace ZiggyCreatures.Caching.Fusion.Internals.Memory;


internal sealed class MemoryCacheAccessor
	: IDisposable
{
	// track whether current Remove call is due to capacity eviction and which policy triggered it
	internal static readonly AsyncLocal<string?> CurrentEvictionPolicyName = new();
	internal static readonly AsyncLocal<string?> CurrentEvictionOperationId = new();

	private readonly IFusionCacheEvictionPolicy? _evictionPolicy;

	public MemoryCacheAccessor(IMemoryCache? memoryCache, FusionCacheOptions options, ILogger? logger, FusionCacheMemoryEventsHub events, IFusionCacheEvictionPolicy? evictionPolicy = null)
	{
		if (memoryCache is not null)
		{
			_cache = memoryCache;
		}
		else
		{
			_cache = new MemoryCache(new MemoryCacheOptions());
			_cacheIsOwned = true;
		}
		// AN ACTUAL CLEAR CAN BE DONE ONLY WHEN THE INNER IMemoryCache
		// IS TOTALLY OWNED (EG: NOT PASSED FROM THE OUTSIDE) AND ITS
		// ACTUAL TYPE IS MemoryCache, WHICH HAS THE Clear() METHOD
		_cacheCanClear = _cacheIsOwned && _cache is MemoryCache;

		_options = options;
		_logger = logger;
		_events = events;
		_evictionPolicy = evictionPolicy;
	}

	private IMemoryCache _cache;
	private readonly bool _cacheIsOwned;
	private readonly bool _cacheCanClear;
	private readonly FusionCacheOptions _options;
	private readonly ILogger? _logger;
	private readonly FusionCacheMemoryEventsHub _events;

	public void UpdateEntryFromDistributedEntry<TValue>(string operationId, string key, FusionCacheMemoryEntry<TValue> memoryEntry, FusionCacheDistributedEntry<TValue> distributedEntry)
	{
		// ACTIVITY
		using var activity = Activities.SourceMemoryLevel.StartActivityWithCommonTags(Activities.Names.MemorySet, _options.CacheName, _options.InstanceId!, key, operationId, CacheLevelKind.Memory);

		memoryEntry.UpdateFromDistributedEntry(distributedEntry);
	}

	public void SetEntry<TValue>(string operationId, string key, IFusionCacheMemoryEntry entry, FusionCacheEntryOptions options, bool skipPhysicalSet = false)
	{
		if (skipPhysicalSet)
			return;

		// IF FAIL-SAFE IS DISABLED AND DURATION IS <= ZERO -> REMOVE ENTRY (WILL SAVE RESOURCES)
		if (options.IsFailSafeEnabled == false && options.Duration <= TimeSpan.Zero)
		{
			RemoveEntry(operationId, key);
			return;
		}

		// ACTIVITY
		using var activity = Activities.SourceMemoryLevel.StartActivityWithCommonTags(Activities.Names.MemorySet, _options.CacheName, _options.InstanceId!, key, operationId, CacheLevelKind.Memory);

		try
		{
			if (_logger?.IsEnabled(LogLevel.Debug) ?? false)
				_logger.Log(LogLevel.Debug, "FUSION [N={CacheName} I={CacheInstanceId}] (O={CacheOperationId} K={CacheKey}): [MC] saving memory entry", _options.CacheName, _options.InstanceId, operationId, key);

			var (memoryEntryOptions, absoluteExpiration) = options.ToMemoryCacheEntryOptionsOrAbsoluteExpiration(_events, _options, _logger, operationId, key, entry.Metadata?.Size, entry.Metadata?.Priority);

			if (memoryEntryOptions is not null)
			{
				_cache.Set<IFusionCacheMemoryEntry>(key, entry, memoryEntryOptions);
			}
			else if (absoluteExpiration is not null)
			{
				_cache.Set<IFusionCacheMemoryEntry>(key, entry, absoluteExpiration.Value);
			}
			else
			{
				throw new InvalidOperationException("No MemoryCacheEntryOptions or AbsoluteExpiration was determined: this should not be possible, WTH!?");
			}

			// update eviction policy tracking and possibly evict
			_evictionPolicy?.OnSet(key, entry.Metadata);
			EvictEntriesIfNeeded(operationId);

			// EVENT
			_events.OnSet(operationId, key);
		}
		catch (Exception exc)
		{
			activity?.SetStatus(ActivityStatusCode.Error, exc.Message);
			activity?.AddException(exc);
			throw;
		}
	}

	public IFusionCacheMemoryEntry? GetEntryOrNull(string operationId, string key)
	{
		// METRIC
		Metrics.CounterMemoryGet.Maybe()?.AddWithCommonTags(1, _options.CacheName, _options.InstanceId!);

		// ACTIVITY
		using var activity = Activities.SourceMemoryLevel.StartActivityWithCommonTags(Activities.Names.MemoryGet, _options.CacheName, _options.InstanceId!, key, operationId, CacheLevelKind.Memory);

		try
		{
			var entry = _cache.Get<IFusionCacheMemoryEntry?>(key);
			if (entry is not null)
			{
				_evictionPolicy?.OnGet(key);
			}

			// EVENT
			if (entry is not null)
			{
				_events.OnHit(operationId, key, entry.IsLogicallyExpired(), activity);
			}
			else
			{
				_events.OnMiss(operationId, key, activity);
			}

			return entry;
		}
		catch (Exception exc)
		{
			activity?.SetStatus(ActivityStatusCode.Error, exc.Message);
			activity?.AddException(exc);
			throw;
		}
	}

	public (IFusionCacheMemoryEntry? entry, bool isValid) TryGetEntry(string operationId, string key)
	{
		// METRIC
		Metrics.CounterMemoryGet.Maybe()?.AddWithCommonTags(1, _options.CacheName, _options.InstanceId!);

		// ACTIVITY
		using var activity = Activities.SourceMemoryLevel.StartActivityWithCommonTags(Activities.Names.MemoryGet, _options.CacheName, _options.InstanceId!, key, operationId, CacheLevelKind.Memory);

		try
		{
			IFusionCacheMemoryEntry? entry;
			bool isValid = false;

			if (_logger?.IsEnabled(LogLevel.Trace) ?? false)
				_logger.Log(LogLevel.Trace, "FUSION [N={CacheName} I={CacheInstanceId}] (O={CacheOperationId} K={CacheKey}): [MC] trying to get from memory", _options.CacheName, _options.InstanceId, operationId, key);

			if (_cache.TryGetValue<IFusionCacheMemoryEntry>(key, out entry) == false)
			{
				if (_logger?.IsEnabled(LogLevel.Debug) ?? false)
					_logger.Log(LogLevel.Debug, "FUSION [N={CacheName} I={CacheInstanceId}] (O={CacheOperationId} K={CacheKey}): [MC] memory entry not found", _options.CacheName, _options.InstanceId, operationId, key);
			}
			else
			{
				if (entry.IsLogicallyExpired())
				{
					if (_logger?.IsEnabled(LogLevel.Debug) ?? false)
						_logger.Log(LogLevel.Debug, "FUSION [N={CacheName} I={CacheInstanceId}] (O={CacheOperationId} K={CacheKey}): [MC] memory entry found (expired) {Entry}", _options.CacheName, _options.InstanceId, operationId, key, entry.ToLogString(_options.IncludeTagsInLogs));
				}
				else
				{
					if (_logger?.IsEnabled(LogLevel.Debug) ?? false)
						_logger.Log(LogLevel.Debug, "FUSION [N={CacheName} I={CacheInstanceId}] (O={CacheOperationId} K={CacheKey}): [MC] memory entry found {Entry}", _options.CacheName, _options.InstanceId, operationId, key, entry.ToLogString(_options.IncludeTagsInLogs));

					isValid = true;
				}
			}

			if (entry is not null)
			{
				_evictionPolicy?.OnGet(key);
				_events.OnHit(operationId, key, isValid == false, activity);
			}
			else
			{
				_events.OnMiss(operationId, key, activity);
			}

			return (entry, isValid);
		}
		catch (Exception exc)
		{
			activity?.SetStatus(ActivityStatusCode.Error, exc.Message);
			activity?.AddException(exc);
			throw;
		}
	}

	/// <summary>
	/// Consults the configured eviction policy (if any) and removes entries if capacity limits are reached.
	/// This will trigger eviction callbacks with <see cref="EvictionReason.Capacity"/> and the policy name.
	/// </summary>
	private void EvictEntriesIfNeeded(string operationId)
	{
		if (_evictionPolicy is null)
			return;
		foreach (var key in _evictionPolicy.GetKeysToEvict())
		{
			// indicate to the eviction callback that this remove is capacity-based
			CurrentEvictionOperationId.Value = operationId;
			CurrentEvictionPolicyName.Value = _evictionPolicy.Name;
			// remove from policy tracking first
			_evictionPolicy.OnRemove(key);
			_cache.Remove(key);
			CurrentEvictionOperationId.Value = null;
			CurrentEvictionPolicyName.Value = null;
		}
	}

	public void RemoveEntry(string operationId, string key)
	{
		// ACTIVITY
		using var activity = Activities.SourceMemoryLevel.StartActivityWithCommonTags(Activities.Names.MemoryRemove, _options.CacheName, _options.InstanceId!, key, operationId, CacheLevelKind.Memory);

		try
		{
			if (_logger?.IsEnabled(LogLevel.Debug) ?? false)
				_logger.Log(LogLevel.Debug, "FUSION [N={CacheName} I={CacheInstanceId}] (O={CacheOperationId} K={CacheKey}): [MC] removing memory entry", _options.CacheName, _options.InstanceId, operationId, key);

			// ensure any PostEvictionCallback sees the correct operationId/policy context
			CurrentEvictionOperationId.Value = operationId;
			CurrentEvictionPolicyName.Value = null;
			_evictionPolicy?.OnRemove(key);
			_cache.Remove(key);
			CurrentEvictionOperationId.Value = null;
			// do not raise Eviction event here (it will be raised by the eviction callback); only raise Remove semantics
			_events.OnRemove(operationId, key);
		}
		catch (Exception exc)
		{
			activity?.SetStatus(ActivityStatusCode.Error, exc.Message);
			activity?.AddException(exc);
			throw;
		}
	}

	public bool ExpireEntry(string operationId, string key, long? timestampThreshold)
	{
		// ACTIVITY
		using var activity = Activities.SourceMemoryLevel.StartActivityWithCommonTags(Activities.Names.MemoryExpire, _options.CacheName, _options.InstanceId!, key, operationId, CacheLevelKind.Memory);

		try
		{
			var entry = _cache.Get<IFusionCacheMemoryEntry>(key);

			if (entry is null)
			{
				if (_logger?.IsEnabled(LogLevel.Trace) ?? false)
					_logger.Log(LogLevel.Trace, "FUSION [N={CacheName} I={CacheInstanceId}] (O={CacheOperationId} K={CacheKey}): [MC] memory entry not found: not necessary to expire", _options.CacheName, _options.InstanceId, operationId, key);

				return false;
			}

			if (_logger?.IsEnabled(LogLevel.Trace) ?? false)
				_logger.Log(LogLevel.Trace, "FUSION [N={CacheName} I={CacheInstanceId}] (O={CacheOperationId} K={CacheKey}): [MC] Metadata {Metadata}", _options.CacheName, _options.InstanceId, operationId, key, entry.Metadata);

			if (timestampThreshold is not null && entry.Timestamp >= timestampThreshold.Value)
			{
				if (_logger?.IsEnabled(LogLevel.Trace) ?? false)
					_logger.Log(LogLevel.Trace, "FUSION [N={CacheName} I={CacheInstanceId}] (O={CacheOperationId} K={CacheKey}): [MC] timestamp of cached memory entry {TimestampCached} was greater than the specified threshold {TimestampThreshold}", _options.CacheName, _options.InstanceId, operationId, key, entry.Timestamp, timestampThreshold.Value);

				return false;
			}

			if (_logger?.IsEnabled(LogLevel.Debug) ?? false)
				_logger.Log(LogLevel.Debug, "FUSION [N={CacheName} I={CacheInstanceId}] (O={CacheOperationId} K={CacheKey}): [MC] expiring memory entry", _options.CacheName, _options.InstanceId, operationId, key);

			// MAKE THE ENTRY LOGICALLY EXPIRE
			entry.LogicalExpirationTimestamp = 0L;
			if (entry.Metadata is not null)
			{
				entry.Metadata.IsStale = true;
				entry.Metadata.EagerExpirationTimestamp = null;
			}

			// EVENT
			_events.OnExpire(operationId, key);

			return true;
		}
		catch (Exception exc)
		{
			activity?.SetStatus(ActivityStatusCode.Error, exc.Message);
			activity?.AddException(exc);
			throw;
		}
	}

	public bool CanClear
	{
		get { return _cacheCanClear; }
	}

	public bool TryClear()
	{
		if (_cacheCanClear == false)
			return false;

		((MemoryCache)_cache).Clear();
		return true;
	}

	// IDISPOSABLE
	private bool _disposedValue = false;
	private void Dispose(bool disposing)
	{
		if (!_disposedValue)
		{
			if (disposing)
			{
				if (_cacheIsOwned)
				{
					(_cache as MemoryCache)?.Compact(1);
					_cache.Dispose();
				}
			}
#pragma warning disable CS8625 // Cannot convert null literal to non-nullable reference type.
			_cache = null;
#pragma warning restore CS8625 // Cannot convert null literal to non-nullable reference type.
			_disposedValue = true;
		}
	}

	public void Dispose()
	{
		Dispose(true);
	}
}
