using System;
using System.Collections.Generic;
using ZiggyCreatures.Caching.Fusion.Internals;

namespace ZiggyCreatures.Caching.Fusion;

/// <summary>
/// A straightforward least-recently-used eviction policy.
/// </summary>
public class LruEvictionPolicy
	: IFusionCacheEvictionPolicy
{
	private readonly LinkedList<string> _usageList = new();
	private readonly Dictionary<string, LinkedListNode<string>> _nodes = new();
	private readonly object _lock = new();

	/// <inheritdoc/>
	public string Name => "LRU";

	/// <inheritdoc/>
	public FusionCacheEvictionPolicyConfig Config { get; }

	/// <summary>
	/// Constructs a new <see cref="LruEvictionPolicy"/> with the specified configuration.
	/// </summary>
	public LruEvictionPolicy(FusionCacheEvictionPolicyConfig config)
	{
		Config = config ?? throw new ArgumentNullException(nameof(config));
	}

	/// <inheritdoc/>
	public void OnGet(string key)
	{
		lock (_lock)
		{
			if (_nodes.TryGetValue(key, out var node))
			{
				_usageList.Remove(node);
				_usageList.AddFirst(node);
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
				// update existing
				_usageList.Remove(existing);
			}
			var node = _usageList.AddFirst(key);
			_nodes[key] = node;
		}
	}

	/// <inheritdoc/>
	public void OnRemove(string key)
	{
		lock (_lock)
		{
			if (_nodes.TryGetValue(key, out var node))
			{
				_usageList.Remove(node);
				_nodes.Remove(key);
			}
		}
	}

	/// <inheritdoc/>
	public IEnumerable<string> GetKeysToEvict()
	{
		lock (_lock)
		{
			if (Config.MaxEntryCount is null)
				yield break;
			var capacity = Config.MaxEntryCount.Value;
			// check threshold
			if (_nodes.Count <= capacity * Config.EvictionThreshold)
				yield break;
			// compute number to evict
			var toEvict = (int)Math.Round(capacity * Config.EvictionPercentage);
			if (toEvict < Config.MinEvictionBatchSize)
				toEvict = Config.MinEvictionBatchSize;
			if (toEvict > Config.MaxEvictionBatchSize)
				toEvict = Config.MaxEvictionBatchSize;
			var count = 0;
			var node = _usageList.Last;
			while (node != null && count < toEvict)
			{
				yield return node.Value;
				node = node.Previous;
				count++;
			}
		}
	}
}
