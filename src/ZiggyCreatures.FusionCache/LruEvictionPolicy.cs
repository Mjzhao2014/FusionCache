using System;
using System.Collections.Generic;
using System.Linq;
using ZiggyCreatures.Caching.Fusion.Internals;

namespace ZiggyCreatures.Caching.Fusion;

/// <summary>
/// An in-memory eviction policy that evicts least recently used items when capacity limits are reached.
/// </summary>
public class LruEvictionPolicy : IFusionCacheEvictionPolicy
{
	private readonly object _lock = new();
	private readonly Dictionary<string, EntryInfo> _entries = new();
	private readonly FusionCacheEvictionPolicyConfig _config;

	internal class EntryInfo
	{
		public long LastAccessTicks;
		public long? Size;
		public long AccessCount;
	}

	/// <summary>
	/// Create a new LRU eviction policy with the given configuration.
	/// </summary>
	/// <param name="config">The policy configuration.</param>
	public LruEvictionPolicy(FusionCacheEvictionPolicyConfig config)
	{
		_config = config;
	}

	/// <inheritdoc/>
	public string Name => "LRU";

	/// <inheritdoc/>
	public FusionCacheEvictionPolicyConfig Config => _config;

	/// <inheritdoc/>
	public void OnGet(string key)
	{
		lock (_lock)
		{
			if (_entries.TryGetValue(key, out var info))
			{
				info.LastAccessTicks = DateTimeOffset.UtcNow.Ticks;
				info.AccessCount++;
			}
		}
	}

	/// <inheritdoc/>
	public void OnSet(string key, FusionCacheEntryMetadata? metadata)
	{
		lock (_lock)
		{
			if (_entries.TryGetValue(key, out var info))
			{
				info.LastAccessTicks = DateTimeOffset.UtcNow.Ticks;
				info.Size = metadata?.Size;
				info.AccessCount++;
			}
			else
			{
				_entries[key] = new EntryInfo
				{
					LastAccessTicks = DateTimeOffset.UtcNow.Ticks,
					Size = metadata?.Size,
					AccessCount = 1
				};
			}
		}
	}

	/// <inheritdoc/>
	public void OnRemove(string key)
	{
		lock (_lock)
		{
			_entries.Remove(key);
		}
	}

	private int GetEvictionCount(int currentCount)
	{
		var count = (int)Math.Ceiling(currentCount * _config.EvictionPercentage);
		if (_config.MinEvictionBatchSize.HasValue && count < _config.MinEvictionBatchSize.Value)
			count = _config.MinEvictionBatchSize.Value;
		if (_config.MaxEvictionBatchSize.HasValue && count > _config.MaxEvictionBatchSize.Value)
			count = _config.MaxEvictionBatchSize.Value;
		return count;
	}

	/// <inheritdoc/>
	public IEnumerable<string> GetKeysToEvict()
	{
		lock (_lock)
		{
			var currentCount = _entries.Count;
			if (_config.MaxEntryCount.HasValue)
			{
				var thresholdCount = (int)(_config.MaxEntryCount.Value * _config.EvictionThreshold);
				if (currentCount <= thresholdCount)
					yield break;
				var toEvict = GetEvictionCount(currentCount);
				foreach (var kvp in _entries.OrderBy(e => e.Value.LastAccessTicks).Take(toEvict))
					yield return kvp.Key;
			}
			else if (_config.MaxTotalSize.HasValue)
			{
				long totalSize = _entries.Values.Sum(e => e.Size ?? 0L);
				var thresholdSize = (long)(_config.MaxTotalSize.Value * _config.EvictionThreshold);
				if (totalSize <= thresholdSize)
					yield break;
				var toEvict = GetEvictionCount(currentCount);
				foreach (var kvp in _entries.OrderBy(e => e.Value.LastAccessTicks).Take(toEvict))
					yield return kvp.Key;
			}
		}
	}
}
