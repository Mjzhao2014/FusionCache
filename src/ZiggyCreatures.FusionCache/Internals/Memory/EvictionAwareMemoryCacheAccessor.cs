using System.Collections.Concurrent;
using System.Collections.Immutable;
using System.Diagnostics;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using ZiggyCreatures.Caching.Fusion.Eviction;
using ZiggyCreatures.Caching.Fusion.Events;
using ZiggyCreatures.Caching.Fusion.Internals.Diagnostics;
using ZiggyCreatures.Caching.Fusion.Internals.Distributed;

namespace ZiggyCreatures.Caching.Fusion.Internals.Memory;

/// <summary>
/// Memory cache accessor that supports pluggable eviction policies for L1 cache.
/// Wraps the standard MemoryCacheAccessor and adds eviction policy integration.
/// </summary>
internal sealed class EvictionAwareMemoryCacheAccessor : IDisposable
{
	private readonly MemoryCacheAccessor _innerAccessor;
	private readonly IFusionCacheEvictionPolicy? _evictionPolicy;
	private readonly ConcurrentDictionary<string, IFusionCacheMemoryEntry> _trackedEntries;
	private readonly FusionCacheOptions _options;
	private readonly ILogger? _logger;
	private readonly FusionCacheMemoryEventsHub _events;
	private long _currentTotalSize;

	public EvictionAwareMemoryCacheAccessor(
		IMemoryCache? memoryCache, 
		FusionCacheOptions options, 
		ILogger? logger, 
		FusionCacheMemoryEventsHub events,
		IFusionCacheEvictionPolicy? evictionPolicy = null)
	{
		_innerAccessor = new MemoryCacheAccessor(memoryCache, options, logger, events);
		_evictionPolicy = evictionPolicy;
		_trackedEntries = new ConcurrentDictionary<string, IFusionCacheMemoryEntry>();
		_options = options;
		_logger = logger;
		_events = events;
	}

	public void UpdateEntryFromDistributedEntry<TValue>(string operationId, string key, FusionCacheMemoryEntry<TValue> memoryEntry, FusionCacheDistributedEntry<TValue> distributedEntry)
	{
		_innerAccessor.UpdateEntryFromDistributedEntry(operationId, key, memoryEntry, distributedEntry);
	}

	public void SetEntry<TValue>(string operationId, string key, IFusionCacheMemoryEntry entry, FusionCacheEntryOptions options, bool skipPhysicalSet = false)
	{
		if (skipPhysicalSet)
			return;

		// Check if eviction is needed before adding new entry
		if (_evictionPolicy != null)
		{
			CheckAndPerformEviction();
		}

		// Set entry in underlying cache
		_innerAccessor.SetEntry<object>(operationId, key, entry, options, skipPhysicalSet);

		// Track entry for eviction policy
		if (_evictionPolicy != null)
		{
			_trackedEntries.AddOrUpdate(key, entry, (k, existing) => entry);
			UpdateTotalSize(entry, null);
			_evictionPolicy.OnEntrySet(key, new FusionCacheEntryInfoWrapper(entry));
		}
	}

	public IFusionCacheMemoryEntry? GetEntryOrNull(string operationId, string key)
	{
		var entry = _innerAccessor.GetEntryOrNull(operationId, key);
		
		// Notify eviction policy of access
		if (entry != null && _evictionPolicy != null)
		{
			_evictionPolicy.OnEntryAccessed(key, new FusionCacheEntryInfoWrapper(entry));
		}

		return entry;
	}

	public (IFusionCacheMemoryEntry? entry, bool isValid) TryGetEntry(string operationId, string key)
	{
		var result = _innerAccessor.TryGetEntry(operationId, key);
		
		// Notify eviction policy of access
		if (result.entry != null && _evictionPolicy != null)
		{
			_evictionPolicy.OnEntryAccessed(key, new FusionCacheEntryInfoWrapper(result.entry));
		}

		return result;
	}

	public void RemoveEntry(string operationId, string key)
	{
		_innerAccessor.RemoveEntry(operationId, key);

		// Update tracking for eviction policy
		if (_evictionPolicy != null)
		{
			if (_trackedEntries.TryRemove(key, out var removedEntry))
			{
				UpdateTotalSize(null, removedEntry);
			}
			_evictionPolicy.OnEntryRemoved(key);
		}
	}

	public bool ExpireEntry(string operationId, string key, long? timestampThreshold)
	{
		return _innerAccessor.ExpireEntry(operationId, key, timestampThreshold);
	}

	public bool CanClear => _innerAccessor.CanClear;

	public bool TryClear()
	{
		var result = _innerAccessor.TryClear();
		
		if (result && _evictionPolicy != null)
		{
			_trackedEntries.Clear();
			_currentTotalSize = 0;
			_evictionPolicy.Reset();
		}

		return result;
	}

	private void CheckAndPerformEviction()
	{
		if (_evictionPolicy == null)
			return;

		var currentEntryCount = _trackedEntries.Count;
		var currentTotalSize = Interlocked.Read(ref _currentTotalSize);

		if (_evictionPolicy.ShouldTriggerEviction(currentEntryCount, currentTotalSize))
		{
			PerformEviction();
		}
	}

	private void PerformEviction()
	{
		if (_evictionPolicy == null)
			return;

		try
		{
			var currentEntryCount = _trackedEntries.Count;
			var currentTotalSize = Interlocked.Read(ref _currentTotalSize);
			var evictionCount = _evictionPolicy.GetEvictionCount(currentEntryCount, currentTotalSize);

			if (evictionCount <= 0)
				return;

			if (_logger?.IsEnabled(LogLevel.Debug) ?? false)
				_logger.Log(LogLevel.Debug, 
					"FUSION [N={CacheName} I={CacheInstanceId}]: [MC-EVICTION] Starting eviction of {EvictionCount} entries (Current: {CurrentCount} entries, {CurrentSize} bytes)", 
					_options.CacheName, _options.InstanceId, evictionCount, currentEntryCount, currentTotalSize);

			// Get current snapshot of entries for eviction selection
			var currentEntries = _trackedEntries.ToImmutableDictionary()
				.ToDictionary(kvp => kvp.Key, kvp => (IFusionCacheEntryInfo)new FusionCacheEntryInfoWrapper(kvp.Value));
			var keysToEvict = _evictionPolicy.SelectEntriesForEviction(currentEntries, evictionCount);

			var actualEvictedCount = 0;
			foreach (var keyToEvict in keysToEvict)
			{
				try
				{
					// Generate operation ID for eviction
					var evictionOperationId = Guid.NewGuid().ToString("N");
					
					// Get the entry value before removal for the event
					object? entryValue = null;
					if (_trackedEntries.TryGetValue(keyToEvict, out var entryToEvict))
					{
						entryValue = entryToEvict.Value;
					}

					RemoveEntry(evictionOperationId, keyToEvict);
					actualEvictedCount++;

					// Fire eviction event
					_events.OnPolicyEviction(evictionOperationId, keyToEvict, _evictionPolicy.Name, entryValue);

					if (_logger?.IsEnabled(LogLevel.Trace) ?? false)
						_logger.Log(LogLevel.Trace, 
							"FUSION [N={CacheName} I={CacheInstanceId}] (O={CacheOperationId} K={CacheKey}): [MC-EVICTION] Evicted entry", 
							_options.CacheName, _options.InstanceId, evictionOperationId, keyToEvict);
				}
				catch (Exception exc)
				{
					if (_logger?.IsEnabled(LogLevel.Warning) ?? false)
						_logger.Log(LogLevel.Warning, exc, 
							"FUSION [N={CacheName} I={CacheInstanceId}] (K={CacheKey}): [MC-EVICTION] Error evicting entry", 
							_options.CacheName, _options.InstanceId, keyToEvict);
				}
			}

			if (_logger?.IsEnabled(LogLevel.Debug) ?? false)
				_logger.Log(LogLevel.Debug, 
					"FUSION [N={CacheName} I={CacheInstanceId}]: [MC-EVICTION] Completed eviction of {ActualEvictedCount}/{RequestedEvictionCount} entries", 
					_options.CacheName, _options.InstanceId, actualEvictedCount, evictionCount);
		}
		catch (Exception exc)
		{
			if (_logger?.IsEnabled(LogLevel.Error) ?? false)
				_logger.Log(LogLevel.Error, exc, 
					"FUSION [N={CacheName} I={CacheInstanceId}]: [MC-EVICTION] Error during eviction process", 
					_options.CacheName, _options.InstanceId);
		}
	}

	private void UpdateTotalSize(IFusionCacheMemoryEntry? addedEntry, IFusionCacheMemoryEntry? removedEntry)
	{
		long sizeChange = 0;

		if (addedEntry != null)
		{
			sizeChange += GetEntrySize(addedEntry);
		}

		if (removedEntry != null)
		{
			sizeChange -= GetEntrySize(removedEntry);
		}

		if (sizeChange != 0)
		{
			Interlocked.Add(ref _currentTotalSize, sizeChange);
		}
	}

	private static long GetEntrySize(IFusionCacheMemoryEntry entry)
	{
		// Use size from metadata if available
		if (entry.Metadata?.Size.HasValue == true)
		{
			return entry.Metadata.Size.Value;
		}

		// Default size estimate
		return 1024; // 1KB default
	}

	// IDISPOSABLE
	private bool _disposedValue = false;
	private void Dispose(bool disposing)
	{
		if (!_disposedValue)
		{
			if (disposing)
			{
				_innerAccessor?.Dispose();
				_evictionPolicy?.Dispose();
				_trackedEntries?.Clear();
			}
			_disposedValue = true;
		}
	}

	public void Dispose()
	{
		Dispose(true);
	}
}