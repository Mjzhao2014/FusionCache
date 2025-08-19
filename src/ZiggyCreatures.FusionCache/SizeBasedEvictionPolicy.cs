using System;
using System.Collections.Generic;
using System.Linq;
using ZiggyCreatures.Caching.Fusion.Internals;

namespace ZiggyCreatures.Caching.Fusion;

/// <summary>
/// An in-memory eviction policy that evicts the largest entries first to maximize capacity when size limits are reached.
/// </summary>
public class SizeBasedEvictionPolicy : IFusionCacheEvictionPolicy
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
	/// Create a new size-based eviction policy with the given configuration.
	/// </summary>
	/// <param name="config">The policy configuration.</param>
	public SizeBasedEvictionPolicy(FusionCacheEvictionPolicyConfig config)
	{
		_config = config;
	}

	/// <inheritdoc/>
	public string Name => "SIZE";

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

	/// <inheritdoc/>
	public IEnumerable<string> GetKeysToEvict()
	{
		lock (_lock)
		{
			if (!_config.MaxTotalSize.HasValue)
			{
				// nothing to do if no max total size configured
				yield break;
			}
			long totalSize = _entries.Values.Sum(e => e.Size ?? 0L);
			var thresholdSize = (long)(_config.MaxTotalSize.Value * _config.EvictionThreshold);
			if (totalSize <= thresholdSize)
				yield break;
			long targetSize = thresholdSize;
			// Determine max number to evict by configured percentage and min/max constraints
			var currentCount = _entries.Count;
			var nominalCountToEvict = (int)Math.Ceiling(currentCount * _config.EvictionPercentage);
			if (_config.MinEvictionBatchSize.HasValue && nominalCountToEvict < _config.MinEvictionBatchSize.Value)
				nominalCountToEvict = _config.MinEvictionBatchSize.Value;
			if (_config.MaxEvictionBatchSize.HasValue && nominalCountToEvict > _config.MaxEvictionBatchSize.Value)
				nominalCountToEvict = _config.MaxEvictionBatchSize.Value;
			int evicted = 0;
			foreach (var kvp in _entries.OrderByDescending(e => e.Value.Size ?? 0L))
			{
				if (evicted >= nominalCountToEvict)
					break;
				yield return kvp.Key;
				totalSize -= (kvp.Value.Size ?? 0L);
				evicted++;
				if (totalSize <= targetSize)
					break;
			}
		}
	}
}
