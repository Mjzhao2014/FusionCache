using ZiggyCreatures.Caching.Fusion.Internals;

namespace ZiggyCreatures.Caching.Fusion;

/// <summary>
/// A size-based eviction policy that removes the largest entries first when capacity thresholds are exceeded.
/// Useful when keeping overall cached data size below a specified limit.
/// </summary>
public sealed class SizeBasedEvictionPolicy
	: IFusionCacheEvictionPolicy
{
	private readonly Dictionary<string, long> _sizes = new();
	private long _currentSize;
	private readonly object _lock = new object();
	public string Name => "Size";
	public FusionCacheEvictionPolicyConfig Config { get; }

	public SizeBasedEvictionPolicy(FusionCacheEvictionPolicyConfig config)
	{
		Config = config ?? throw new ArgumentNullException(nameof(config));
	}

	public void OnGet(string key)
	{
		// no-op for size-based policy
	}
	public void OnSet(string key, FusionCacheEntryMetadata? metadata)
	{
		var size = metadata?.Size ?? 0;
		lock (_lock)
		{
			if (_sizes.TryGetValue(key, out var existingSize))
			{
				_currentSize -= existingSize;
			}
			_sizes[key] = size;
			_currentSize += size;
		}
	}
	public void OnRemove(string key)
	{
		lock (_lock)
		{
			if (_sizes.TryGetValue(key, out var s))
			{
				_currentSize -= s;
				_sizes.Remove(key);
			}
		}
	}
	public IEnumerable<string> GetKeysToEvict()
	{
		var keys = new List<string>();
		lock (_lock)
		{
			int count = _sizes.Count;
			long size = _currentSize;
			var maxCount = Config.MaxEntryCount;
			var maxSize = Config.MaxTotalSize;
			bool overCount = maxCount.HasValue && count > (int)Math.Ceiling(maxCount.Value * Config.EvictionThreshold);
			bool overSize = maxSize.HasValue && size > (long)Math.Ceiling(maxSize.Value * Config.EvictionThreshold);
			if (overCount == false && overSize == false)
				return keys;
			// sort keys by size descending
			var ordered = _sizes.OrderByDescending(kv => kv.Value);
			int removed = 0;
			long freed = 0;
			foreach (var kvp in ordered)
			{
				keys.Add(kvp.Key);
				removed++;
				freed += kvp.Value;
				if (removed >= Config.MinEvictionBatchSize)
				{
					int newCount = count - removed;
					long newSize = size - freed;
					overCount = maxCount.HasValue && newCount > (int)Math.Ceiling(maxCount.Value * Config.EvictionThreshold);
					overSize = maxSize.HasValue && newSize > (long)Math.Ceiling(maxSize.Value * Config.EvictionThreshold);
					if (overCount == false && overSize == false)
						break;
				}
				if (removed >= Config.MaxEvictionBatchSize)
					break;
			}
		}
		return keys;
	}
}
