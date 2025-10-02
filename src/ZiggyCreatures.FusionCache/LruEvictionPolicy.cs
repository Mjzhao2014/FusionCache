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
		Config.Validate();
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
		List<string>? snapshot = null;
		lock (_lock)
		{
			var toEvict = Config.CalculateEvictionBatchSize(_nodes.Count);
			if (toEvict > 0)
			{
				snapshot = new List<string>(Math.Min(toEvict, _nodes.Count));
				var node = _usageList.Last;
				while (node != null && snapshot.Count < toEvict)
				{
					snapshot.Add(node.Value);
					node = node.Previous;
				}
			}
		}

		if (snapshot == null)
			yield break;

		foreach (var key in snapshot)
		{
			yield return key;
		}
	}

	/// <inheritdoc/>
	public IFusionCacheEvictionPolicy Duplicate()
	{
		return new LruEvictionPolicy(Config.Duplicate());
	}
}
