using System;
using System.Collections.Generic;
using ZiggyCreatures.Caching.Fusion.Internals;

namespace ZiggyCreatures.Caching.Fusion
{
	/// <summary>
	/// A simple least recently used eviction policy based on a linked list and lookup dictionary.
	/// Maintains O(1) cost for get/set/remove.
	/// </summary>
	public class LruEvictionPolicy : IFusionCacheEvictionPolicy
	{
		private readonly LinkedList<string> _lruList = new();
		private readonly Dictionary<string, LinkedListNode<string>> _nodes = new(StringComparer.Ordinal);
		private readonly object _lock = new();
		public LruEvictionPolicy(FusionCacheEvictionPolicyConfig config)
		{
			Config = config ?? throw new ArgumentNullException(nameof(config));
		}
		public string Name => "LRU";
		public FusionCacheEvictionPolicyConfig Config { get; }
		public void OnGet(string key)
		{
			lock (_lock)
			{
				if (_nodes.TryGetValue(key, out var node))
				{
					// move to most-recently-used (end of linked list)
					_lruList.Remove(node);
					_lruList.AddLast(node);
				}
			}
		}
		public void OnSet(string key, FusionCacheEntryMetadata? metadata)
		{
			lock (_lock)
			{
				if (_nodes.TryGetValue(key, out var node))
				{
					_lruList.Remove(node);
					_lruList.AddLast(node);
				}
				else
				{
					var newNode = new LinkedListNode<string>(key);
					_lruList.AddLast(newNode);
					_nodes[key] = newNode;
				}
			}
		}
		public void OnRemove(string key)
		{
			lock (_lock)
			{
				if (_nodes.TryGetValue(key, out var node))
				{
					_lruList.Remove(node);
					_nodes.Remove(key);
				}
			}
		}
		public IEnumerable<string> GetKeysToEvict()
		{
			if (Config.MaxEntryCount is null)
				return Array.Empty<string>();
			lock (_lock)
			{
				int count = _nodes.Count;
				int max = Config.MaxEntryCount.Value;
				if (count == 0)
					return Array.Empty<string>();
				double threshold = Config.EvictionThreshold;
				if (count < max * threshold)
					return Array.Empty<string>();
				int evictCount = (int)Math.Ceiling(Config.EvictionPercentage * max);
				evictCount = Math.Max(evictCount, Config.MinEvictionBatchSize);
				evictCount = Math.Min(evictCount, Config.MaxEvictionBatchSize);
				evictCount = Math.Min(evictCount, count);
				if (evictCount <= 0)
					return Array.Empty<string>();
				var keys = new List<string>(evictCount);
				for (int i = 0; i < evictCount; i++)
				{
					var node = _lruList.First;
					if (node is null)
						break;
					string k = node.Value;
					_lruList.RemoveFirst();
					_nodes.Remove(k);
					keys.Add(k);
				}
				return keys;
			}
		}
	}
}
