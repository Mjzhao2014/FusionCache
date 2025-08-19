using System;
using System.Collections.Generic;
using Microsoft.Extensions.Caching.Memory;
using ZiggyCreatures.Caching.Fusion.Events;
using ZiggyCreatures.Caching.Fusion.Internals.Memory;

namespace ZiggyCreatures.Caching.Fusion;

/// <summary>
/// Least Recently Used eviction policy: maintains a list of entries
/// ordered by recent access time and removes the least recently used
/// entries when capacity limits are reached.
/// </summary>
public class LruEvictionPolicy
	: IFusionCacheEvictionPolicy
{
	private readonly object _lockObj = new();
	private readonly LinkedList<string> _lruList = new();
	private readonly Dictionary<string, LinkedListNode<string>> _lruMap = new();
	private readonly Dictionary<string, long> _sizes = new();
	private long _totalSize = 0;

	/// <inheritdoc/>
	public string Name => "LRU";

	/// <inheritdoc/>
	public FusionCacheEvictionPolicyConfig Config { get; }

	public LruEvictionPolicy(FusionCacheEvictionPolicyConfig config)
	{
		Config = config;
	}

	/// <inheritdoc/>
	public void OnSet(string key, long size)
	{
		lock (_lockObj)
		{
			// remove existing node if present
			if (_lruMap.TryGetValue(key, out var existing))
			{
				_lruList.Remove(existing);
				_lruMap.Remove(key);
			}
			var node = _lruList.AddFirst(key);
			_lruMap[key] = node;
			// update size tracking
			long oldSize = 0;
			if (_sizes.TryGetValue(key, out var prevSize))
			{
				oldSize = prevSize;
			}
			// update size from provided metadata
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
			if (_lruMap.TryGetValue(key, out var node))
			{
				// move to front
				_lruList.Remove(node);
				_lruList.AddFirst(node);
			}
		}
	}

	/// <inheritdoc/>
	public void OnRemove(string key)
	{
		lock (_lockObj)
		{
			if (_lruMap.TryGetValue(key, out var node))
			{
				_lruList.Remove(node);
				_lruMap.Remove(key);
			}
			if (_sizes.TryGetValue(key, out var size))
			{
				_totalSize -= size;
				_sizes.Remove(key);
			}
		}
	}

	private bool ShouldTriggerEviction(int currentCount)
	{
		bool trigger = false;
		if (Config.MaxEntryCount.HasValue && Config.MaxEntryCount.Value > 0)
		{
			var countUsage = (double)currentCount / Config.MaxEntryCount.Value;
			if (countUsage > Config.EvictionThreshold)
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
		List<string> keysToEvict = new();
		lock (_lockObj)
		{
			var count = _lruList.Count;
			if (ShouldTriggerEviction(count) == false)
				return;
			var removeCount = DetermineRemoveCount(count);
			for (int i = 0; i < removeCount && _lruList.Count > 0; i++)
			{
				var last = _lruList.Last!;
				var key = last.Value;
				_lruList.RemoveLast();
				_lruMap.Remove(key);
				if (_sizes.TryGetValue(key, out var size))
				{
					_totalSize -= size;
					_sizes.Remove(key);
				}
				keysToEvict.Add(key);
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
