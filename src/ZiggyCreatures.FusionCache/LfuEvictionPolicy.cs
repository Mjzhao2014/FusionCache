using System;
using System.Collections.Generic;
using System.Linq;

namespace ZiggyCreatures.Caching.Fusion;

/// <summary>
/// An eviction policy based on least-frequently-used order. Entries track number of hits;
/// when eviction is triggered, the entries with the lowest access count will be removed first.
/// Note: ties will be broken arbitrarily.
/// </summary>
public class LfuEvictionPolicy : IFusionCacheEvictionPolicy
{
	private readonly FusionCacheEvictionPolicyConfig _config;
	private readonly Dictionary<string, int> _counts = new();
	private readonly Dictionary<string, long> _sizes = new();
	private long _totalSize = 0;
	private readonly object _lock = new();
	public string Name => "LFU";
	public LfuEvictionPolicy(FusionCacheEvictionPolicyConfig config)
	{
		_config = config ?? throw new ArgumentNullException(nameof(config));
	}
	public void OnAccess(string key)
	{
		lock(_lock)
		{
			if (_counts.TryGetValue(key, out var count))
			{
				_counts[key] = count + 1;
			}
		}
	}
	public void OnSet(string key, long? size)
	{
		lock(_lock)
		{
			_counts[key] = 1;
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
			_counts.Remove(key);
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
			while (NeedsEviction() && _counts.Count > 0)
			{
				// pick the key with the smallest hit count
				var minCount = _counts.Values.Min();
				// in case multiple with same min count, pick arbitrary first
				var key = _counts.First(kvp => kvp.Value == minCount).Key;
				_counts.Remove(key);
				if (_sizes.TryGetValue(key, out var size))
				{
					_totalSize -= size;
					_sizes.Remove(key);
				}
				toEvict.Add(key);
				if (ReachedEvictionBatchLimit(toEvict.Count))
					break;
			}
		}
		return toEvict;
	}
	private bool NeedsEviction()
	{
		if (_config.MaxEntryCount.HasValue)
		{
			var limit = _config.MaxEntryCount.Value;
			var thresholdLimit = (int)Math.Floor(limit * _config.EvictionThreshold);
			if (_counts.Count > thresholdLimit)
				return true;
		}
		if (_config.MaxTotalSize.HasValue)
		{
			var limit = _config.MaxTotalSize.Value;
			var thresholdLimit = (long)Math.Floor(limit * _config.EvictionThreshold);
			if (_totalSize > thresholdLimit)
				return true;
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
