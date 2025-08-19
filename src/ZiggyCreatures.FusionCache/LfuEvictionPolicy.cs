using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Caching.Memory;
using ZiggyCreatures.Caching.Fusion.Events;
using ZiggyCreatures.Caching.Fusion.Internals.Memory;

namespace ZiggyCreatures.Caching.Fusion;

/// <summary>
/// Least Frequently Used eviction policy: tracks an access count per key
/// and removes the entries with the lowest access counts when capacity
/// limits are reached.
/// </summary>
public class LfuEvictionPolicy
	: IFusionCacheEvictionPolicy
{
	private readonly object _lockObj = new();
	private readonly Dictionary<string, int> _counts = new();
	private readonly Dictionary<string, long> _sizes = new();
	private readonly Dictionary<string, long> _lastAccess = new();
	private long _totalSize = 0;

	/// <inheritdoc/>
	public string Name => "LFU";

	/// <inheritdoc/>
	public FusionCacheEvictionPolicyConfig Config { get; }

	public LfuEvictionPolicy(FusionCacheEvictionPolicyConfig config)
	{
		Config = config;
	}

	/// <inheritdoc/>
	public void OnSet(string key, long size)
	{
		lock (_lockObj)
		{
			_counts[key] = 1;
			_lastAccess[key] = DateTimeOffset.UtcNow.Ticks;
			long oldSize = 0;
			if (_sizes.TryGetValue(key, out var prevSize))
			{
				oldSize = prevSize;
			}
			var entrySize = size;
			_sizes[key] = entrySize;
			_totalSize += entrySize - oldSize;
		}
	}

	/// <inheritdoc/>
	public void OnAccess(string key)
	{
		lock (_lockObj)
		{
			if (_counts.TryGetValue(key, out var count))
			{
				_counts[key] = count + 1;
				_lastAccess[key] = DateTimeOffset.UtcNow.Ticks;
			}
		}
	}

	/// <inheritdoc/>
	public void OnRemove(string key)
	{
		lock (_lockObj)
		{
			_counts.Remove(key);
			_lastAccess.Remove(key);
			if (_sizes.TryGetValue(key, out var size))
			{
				_totalSize -= size;
				_sizes.Remove(key);
			}
		}
	}

	private bool ShouldTriggerEviction(int count)
	{
		bool trigger = false;
		if (Config.MaxEntryCount.HasValue && Config.MaxEntryCount.Value > 0)
		{
			var usage = (double)count / Config.MaxEntryCount.Value;
			if (usage > Config.EvictionThreshold)
				trigger = true;
		}
		if (Config.MaxTotalSize.HasValue && Config.MaxTotalSize.Value > 0)
		{
			var sizeUsage = (double)_totalSize / Config.MaxTotalSize.Value;
			if (sizeUsage > Config.EvictionThreshold)
				trigger = true;
		}
		return trigger;
	}

	private int DetermineRemoveCount(int currentCount)
	{
		var countToRemove = (int)Math.Ceiling(currentCount * Config.EvictionPercentage);
		if (Config.MinBatchSize.HasValue && countToRemove < Config.MinBatchSize.Value)
			countToRemove = Config.MinBatchSize.Value;
		if (Config.MaxBatchSize.HasValue && countToRemove > Config.MaxBatchSize.Value)
			countToRemove = Config.MaxBatchSize.Value;
		if (countToRemove < 1)
			countToRemove = 1;
		return countToRemove;
	}

	/// <inheritdoc/>
	public void ApplyPolicy(IMemoryCache memoryCache, FusionCacheMemoryEventsHub events, string operationId)
	{
		List<string> keysToEvict;
		lock (_lockObj)
		{
			var count = _counts.Count;
			if (ShouldTriggerEviction(count) == false)
				return;
			var removeCount = DetermineRemoveCount(count);
			// order by count ascending then by last access
			keysToEvict = _counts.OrderBy(kvp => kvp.Value)
				.ThenBy(kvp => _lastAccess.TryGetValue(kvp.Key, out var ts) ? ts : 0)
				.Take(removeCount)
				.Select(kvp => kvp.Key)
				.ToList();
			foreach (var key in keysToEvict)
			{
				_counts.Remove(key);
				_lastAccess.Remove(key);
				if (_sizes.TryGetValue(key, out var size))
				{
					_totalSize -= size;
					_sizes.Remove(key);
				}
			}
		}
		foreach (var key in keysToEvict)
		{
			if (memoryCache.TryGetValue<IFusionCacheMemoryEntry>(key, out var entry))
			{
				memoryCache.Remove(key);
				events.OnPolicyEviction(operationId, key, Name, entry?.Value);
			}
			else
			{
				memoryCache.Remove(key);
				events.OnPolicyEviction(operationId, key, Name, null);
			}
		}
	}
}
