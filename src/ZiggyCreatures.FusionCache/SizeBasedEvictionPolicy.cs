using System;
using System.Collections.Generic;
using System.Linq;

namespace ZiggyCreatures.Caching.Fusion;

/// <summary>
/// An eviction policy that removes the largest entries first to reclaim memory when size-based
/// capacity thresholds are exceeded.
/// </summary>
public class SizeBasedEvictionPolicy : IFusionCacheEvictionPolicy
{
	private readonly FusionCacheEvictionPolicyConfig _config;
	private readonly Dictionary<string, long> _sizes = new();
	private long _totalSize = 0;
	private readonly object _lock = new();
	public string Name => "Size";
	public SizeBasedEvictionPolicy(FusionCacheEvictionPolicyConfig config)
	{
		_config = config ?? throw new ArgumentNullException(nameof(config));
	}
	public void OnAccess(string key)
	{
		// Size-based eviction does not track accesses.
	}
	public void OnSet(string key, long? size)
	{
		lock(_lock)
		{
			if (_sizes.TryGetValue(key, out var oldSize))
			{
				_totalSize -= oldSize;
			}
			long usedSize = size ?? 1;
			_sizes[key] = usedSize;
			_totalSize += usedSize;
		}
	}
	public void OnRemove(string key)
	{
		lock(_lock)
		{
			if (_sizes.TryGetValue(key, out var size))
			{
				_totalSize -= size;
				_sizes.Remove(key);
			}
		}
	}
	public IEnumerable<string> GetEvictionCandidates()
	{
		var toEvict = new List<string>();
		lock(_lock)
		{
			while (NeedsEviction() && _sizes.Count > 0)
			{
				// pick the entry with the largest size
				var largest = _sizes.OrderByDescending(kvp => kvp.Value).First();
				_sizes.Remove(largest.Key);
				_totalSize -= largest.Value;
				toEvict.Add(largest.Key);
				if (ReachedEvictionBatchLimit(toEvict.Count))
					break;
			}
		}
		return toEvict;
	}
	private bool NeedsEviction()
	{
		if (_config.MaxTotalSize.HasValue)
		{
			var limit = _config.MaxTotalSize.Value;
			var thresholdLimit = (long)Math.Floor(limit * _config.EvictionThreshold);
			return _totalSize > thresholdLimit;
		}
		// if configured only on count, we don't apply size-based eviction
		if (_config.MaxEntryCount.HasValue)
		{
			var limit = _config.MaxEntryCount.Value;
			var thresholdLimit = (int)Math.Floor(limit * _config.EvictionThreshold);
			return _sizes.Count > thresholdLimit;
		}
		return false;
	}
	private bool ReachedEvictionBatchLimit(int evicted)
	{
		if (_config.MaxEvictionBatchSize.HasValue && evicted >= _config.MaxEvictionBatchSize.Value)
			return true;
		return false;
	}
}
