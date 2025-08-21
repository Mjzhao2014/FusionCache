using System.Collections.Generic;
using ZiggyCreatures.Caching.Fusion.Internals;

namespace ZiggyCreatures.Caching.Fusion;

/// <summary>
/// Implements a classic least-recently-used eviction policy: entries that have
/// not been accessed for the longest time will be evicted first when capacity
/// limits are exceeded.
/// </summary>
public class LruEvictionPolicy
	: IFusionCacheEvictionPolicy
{
	private readonly LinkedList<string> _usage = new();
	private readonly Dictionary<string, LinkedListNode<string>> _nodes = new();
	private readonly object _lock = new();

	/// <summary>
	/// Instantiates a new LRU eviction policy using the provided configuration.
	/// </summary>
	public LruEvictionPolicy(FusionCacheEvictionPolicyConfig config)
	{
		Config = config;
	}

	/// <inheritdoc/>
	public string Name => "LRU";

	/// <inheritdoc/>
	public FusionCacheEvictionPolicyConfig Config { get; }

	/// <inheritdoc/>
	public void OnGet(string key)
	{
		lock (_lock)
		{
			if (_nodes.TryGetValue(key, out var node))
			{
				// Move to front as most recently used
				_usage.Remove(node);
				_usage.AddFirst(node);
			}
		}
	}

	/// <inheritdoc/>
	public void OnSet(string key, FusionCacheEntryMetadata? metadata)
	{
		lock (_lock)
		{
			if (_nodes.TryGetValue(key, out var existing))
			{
				_usage.Remove(existing);
			}
			var node = new LinkedListNode<string>(key);
			_usage.AddFirst(node);
			_nodes[key] = node;
		}
	}

	/// <inheritdoc/>
	public void OnRemove(string key)
	{
		lock (_lock)
		{
			if (_nodes.TryGetValue(key, out var existing))
			{
				_usage.Remove(existing);
				_nodes.Remove(key);
			}
		}
	}

	/// <inheritdoc/>
	public IEnumerable<string> GetKeysToEvict()
	{
		if (Config.MaxEntryCount is null)
			return Array.Empty<string>();

		lock (_lock)
		{
			var count = _nodes.Count;
			var max = Config.MaxEntryCount.Value;
			var triggerCount = (int)Math.Floor(max * Config.EvictionThreshold);
			if (count <= triggerCount)
				return Array.Empty<string>();

			var batch = (int)Math.Ceiling(max * Config.EvictionPercentage);
			batch = Math.Max(batch, Config.MinEvictionBatchSize);
			batch = Math.Min(batch, Config.MaxEvictionBatchSize);

			var result = new List<string>(batch);
			var node = _usage.Last;
			while (node is not null && result.Count < batch)
			{
				result.Add(node.Value);
				node = node.Previous;
			}

			return result;
		}
	}
}
