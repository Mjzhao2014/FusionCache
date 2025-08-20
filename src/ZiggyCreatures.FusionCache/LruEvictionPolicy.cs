using System;
using System.Collections.Generic;
using ZiggyCreatures.Caching.Fusion.Internals;

namespace ZiggyCreatures.Caching.Fusion
{
	/// <summary>
	/// Least Recently Used eviction policy implementation using a linked list for O(1) touch and remove per entry.
	/// </summary>
	public class LruEvictionPolicy : IFusionCacheEvictionPolicy
	{
		private readonly object _lock = new();
		private readonly LinkedList<string> _usageList = new();
		private readonly Dictionary<string, LinkedListNode<string>> _nodeMap = new();
		private int _count;
		public string Name => "LRU";
		public FusionCacheEvictionPolicyConfig Config { get; }
		public LruEvictionPolicy(FusionCacheEvictionPolicyConfig config)
		{
			Config = config ?? throw new ArgumentNullException(nameof(config));
		}
		public void OnGet(string key)
		{
			if (key is null)
				return;
			lock (_lock)
			{
				if (_nodeMap.TryGetValue(key, out var node))
				{
					// move to front
					_usageList.Remove(node);
					_usageList.AddFirst(node);
				}
			}
		}
		public void OnSet(string key, FusionCacheEntryMetadata? metadata)
		{
			if (key is null)
				return;
			lock (_lock)
			{
				if (_nodeMap.TryGetValue(key, out var node))
				{
					// existing: move to front
					_usageList.Remove(node);
					_usageList.AddFirst(node);
				}
				else
				{
					node = new LinkedListNode<string>(key);
					_nodeMap[key] = node;
					_usageList.AddFirst(node);
					_count++;
				}
			}
		}
		public void OnRemove(string key)
		{
			if (key is null)
				return;
			lock (_lock)
			{
				if (_nodeMap.TryGetValue(key, out var node))
				{
					_usageList.Remove(node);
					_nodeMap.Remove(key);
					_count--;
				}
			}
		}
		public IEnumerable<string> GetKeysToEvict()
		{
			var keys = new List<string>();
			if (Config.MaxEntryCount is null)
				return keys;
			lock (_lock)
			{
				if (_count <= 0)
					return keys;
				var thresholdCount = (int)Math.Floor(Config.MaxEntryCount.Value * Config.EvictionThreshold);
				if (_count < thresholdCount)
					return keys;
				var evictCount = (int)Math.Ceiling(Config.MaxEntryCount.Value * Config.EvictionPercentage);
				if (evictCount < Config.MinEvictionBatchSize)
					evictCount = Config.MinEvictionBatchSize;
				if (evictCount > Config.MaxEvictionBatchSize)
					evictCount = Config.MaxEvictionBatchSize;
				// ensure we do not try to evict more than we have
				evictCount = Math.Min(evictCount, _count);
				for (var i = 0; i < evictCount; i++)
				{
					if (_usageList.Last is null)
						break;
					var key = _usageList.Last.Value;
					_usageList.RemoveLast();
					_nodeMap.Remove(key);
					_count--;
					keys.Add(key);
				}
			}
			return keys;
		}
	}
}
