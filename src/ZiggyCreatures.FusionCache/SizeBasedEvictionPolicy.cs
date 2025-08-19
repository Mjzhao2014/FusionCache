using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Caching.Memory;
using ZiggyCreatures.Caching.Fusion.Events;
using ZiggyCreatures.Caching.Fusion.Internals.Memory;

namespace ZiggyCreatures.Caching.Fusion;

/// <summary>
/// Size-based eviction policy: removes the largest entries first to attempt
/// to drop total cached size under the configured limit.
/// </summary>
public class SizeBasedEvictionPolicy
	: IFusionCacheEvictionPolicy
{
	private readonly object _lockObj = new();
	private readonly Dictionary<string, long> _sizes = new();
	private long _totalSize = 0;

	/// <inheritdoc/>
	public string Name => "Size";

	/// <inheritdoc/>
	public FusionCacheEvictionPolicyConfig Config { get; }

	public SizeBasedEvictionPolicy(FusionCacheEvictionPolicyConfig config)
	{
		Config = config;
	}

	private bool ShouldTriggerEviction()
	{
		if (Config.MaxTotalSize.HasValue && Config.MaxTotalSize.Value > 0)
		{
			var usage = (double)_totalSize / Config.MaxTotalSize.Value;
			return usage > Config.EvictionThreshold;
		}
		return false;
	}

	/// <inheritdoc/>
	public void OnSet(string key, long size)
	{
		lock (_lockObj)
		{
			long oldSize = 0;
			if (_sizes.TryGetValue(key, out var prev))
			{
				oldSize = prev;
			}
			var entrySize = size;
			_sizes[key] = entrySize;
			_totalSize += entrySize - oldSize;
		}
	}

	/// <inheritdoc/>
	public void OnAccess(string key)
	{
		// no-op for size-based
	}

	/// <inheritdoc/>
	public void OnRemove(string key)
	{
		lock (_lockObj)
		{
			if (_sizes.TryGetValue(key, out var size))
			{
				_totalSize -= size;
				_sizes.Remove(key);
			}
		}
	}

	/// <inheritdoc/>
	public void ApplyPolicy(IMemoryCache memoryCache, FusionCacheMemoryEventsHub events, string operationId)
	{
		List<string> keysToEvict = new();
		lock (_lockObj)
		{
			if (ShouldTriggerEviction() == false)
				return;
			if (_sizes.Count == 0)
				return;
			var targetReduceSize = (long)Math.Ceiling(_totalSize * Config.EvictionPercentage);
			var orderedKeys = _sizes.OrderByDescending(kvp => kvp.Value).Select(kvp => kvp.Key);
			long removed = 0;
			foreach (var key in orderedKeys)
			{
				if (removed >= targetReduceSize)
					break;
				if (_sizes.TryGetValue(key, out var size))
				{
					removed += size;
					_totalSize -= size;
					_sizes.Remove(key);
					keysToEvict.Add(key);
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
