using System;
using System.Collections.Generic;

namespace ZiggyCreatures.Caching.Fusion;

/// <summary>
/// An eviction policy based on least-recently-used order. Entries track the order of access;
/// when configured capacity thresholds are exceeded, the least recently used entries will be evicted first.
/// </summary>
public class LruEvictionPolicy : IFusionCacheEvictionPolicy
{
	private readonly FusionCacheEvictionPolicyConfig _config;
	private readonly LinkedList<string> _lruList = new();
	private readonly Dictionary<string, LinkedListNode<string>> _nodes = new();
	private readonly Dictionary<string, long> _sizes = new();
	private long _totalSize = 0;
	private readonly object _lock = new();
	public string Name => "LRU";
	public LruEvictionPolicy(FusionCacheEvictionPolicyConfig config)
	{
		_config = config ?? throw new ArgumentNullException(nameof(config));
	}
	public void OnAccess(string key)
	{
		lock(_lock)
		{
			if (_nodes.TryGetValue(key, out var node))
			{
				// move to end (most recently used)
				_lruList.Remove(node);
				_lruList.AddLast(node);
			}
		}
	}
	public void OnSet(string key, long? size)
	{
		lock(_lock)
		{
			if (_nodes.TryGetValue(key, out var existing))
			{
				// overwrite existing: adjust size if provided
				_lruList.Remove(existing);
				_nodes.Remove(key);
				if (_sizes.TryGetValue(key, out var oldSize))
				{
					_totalSize -= oldSize;
					_sizes.Remove(key);
				}
			}
			var node = new LinkedListNode<string>(key);
			_lruList.AddLast(node);
			_nodes[key] = node;
			long usedSize = size ?? 1;
			_sizes[key] = usedSize;
			_totalSize += usedSize;
		}
	}
	public void OnRemove(string key)
	{
		lock(_lock)
		{
			if (_nodes.TryGetValue(key, out var node))
			{
				_lruList.Remove(node);
				_nodes.Remove(key);
			}
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
			while (NeedsEviction() && _lruList.First is not null)
			{
				var key = _lruList.First.Value;
				_lruList.RemoveFirst();
				_nodes.Remove(key);
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
			if (_nodes.Count > thresholdLimit)
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
	private bool ReachedEvictionBatchLimit(int evictedCount)
	{
		if (_config.MaxEvictionBatchSize.HasValue && evictedCount >= _config.MaxEvictionBatchSize.Value)
			return true;
		return false;
	}
}
