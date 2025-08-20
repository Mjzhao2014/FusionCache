using System;
using System.Collections.Generic;
using ZiggyCreatures.Caching.Fusion.Internals;

namespace ZiggyCreatures.Caching.Fusion
{
	/// <summary>
	/// An eviction policy that removes entries that have not been accessed for the
	/// longest time when capacity limits are reached (Least Recently Used).
	/// Maintains a doubly-linked list of keys to provide O(1) updates on get/set.
	/// </summary>
	public class LruEvictionPolicy : IFusionCacheEvictionPolicy
	{
		private readonly object _lock = new();
		private readonly Dictionary<string, Node> _nodes = new();
		private Node? _head;
		private Node? _tail;
		private int _count;
		private long _totalSize;
		/// <summary>
		/// Construct a new LRU eviction policy.
		/// </summary>
		public LruEvictionPolicy(FusionCacheEvictionPolicyConfig config)
		{
			Config = config ?? throw new ArgumentNullException(nameof(config));
		}
		public string Name => "LRU";
		public FusionCacheEvictionPolicyConfig Config { get; }
		private sealed class Node
		{
			public string Key;
			public long Size;
			public Node? Prev;
			public Node? Next;
			public Node(string key, long size)
			{
				Key = key;
				Size = size;
			}
		}
		private static long GetEntrySize(FusionCacheEntryMetadata? metadata) => metadata?.Size ?? 1;
		public void OnGet(string key)
		{
			lock (_lock)
			{
				if (_nodes.TryGetValue(key, out var node))
				{
					MoveToHead(node);
				}
			}
		}
		public void OnSet(string key, FusionCacheEntryMetadata? metadata)
		{
			lock (_lock)
			{
				if (_nodes.TryGetValue(key, out var node))
				{
					// update size tracking if needed
					var newSize = GetEntrySize(metadata);
					_totalSize += newSize - node.Size;
					node.Size = newSize;
					MoveToHead(node);
				}
				else
				{
					var size = GetEntrySize(metadata);
					node = new Node(key, size);
					_nodes[key] = node;
					AddToHead(node);
					_count++;
					_totalSize += size;
				}
			}
		}
		public void OnRemove(string key)
		{
			lock (_lock)
			{
				if (_nodes.TryGetValue(key, out var node))
				{
					RemoveNode(node);
					_nodes.Remove(key);
					_count--;
					_totalSize -= node.Size;
				}
			}
		}
		private void AddToHead(Node node)
		{
			node.Prev = null;
			node.Next = _head;
			if (_head != null)
			{
				_head.Prev = node;
			}
			_head = node;
			if (_tail == null)
			{
				_tail = node;
			}
		}
		private void RemoveNode(Node node)
		{
			if (node.Prev != null)
			{
				node.Prev.Next = node.Next;
			}
			else
			{
				_head = node.Next;
			}
			if (node.Next != null)
			{
				node.Next.Prev = node.Prev;
			}
			else
			{
				_tail = node.Prev;
			}
			node.Prev = null;
			node.Next = null;
		}
		private void MoveToHead(Node node)
		{
			if (node == _head)
				return;
			RemoveNode(node);
			AddToHead(node);
		}
		public IEnumerable<string> GetKeysToEvict()
		{
			lock (_lock)
			{
				var keys = new List<string>();
				var triggered = false;
				double triggerCount = 0;
				if (Config.MaxEntryCount.HasValue)
				{
					triggerCount = Config.MaxEntryCount.Value * Config.EvictionThreshold;
					if (_count >= triggerCount)
						triggered = true;
				}
				double triggerSize = 0;
				if (Config.MaxTotalSize.HasValue)
				{
					triggerSize = Config.MaxTotalSize.Value * Config.EvictionThreshold;
					if (_totalSize >= triggerSize)
						triggered = true;
				}
				if (!triggered)
				{
					return keys;
				}
				int removedCount = 0;
				long removedSize = 0;
				int targetRemoveCount = (int)Math.Ceiling(_count * Config.EvictionPercentage);
				targetRemoveCount = Math.Max(targetRemoveCount, Config.MinEvictionBatchSize);
				int maxRemove = Config.MaxEvictionBatchSize;
				var current = _tail;
				while (current != null && removedCount < maxRemove)
				{
					keys.Add(current.Key);
					removedCount++;
					removedSize += current.Size;
					current = current.Prev;
					if (removedCount >= targetRemoveCount)
					{
						bool belowCount = true;
						if (Config.MaxEntryCount.HasValue)
						{
							belowCount = _count - removedCount < Config.MaxEntryCount.Value * Config.EvictionThreshold;
						}
						bool belowSize = true;
						if (Config.MaxTotalSize.HasValue)
						{
							belowSize = _totalSize - removedSize < Config.MaxTotalSize.Value * Config.EvictionThreshold;
						}
						if (belowCount && belowSize)
							break;
					}
				}
				return keys;
			}
		}
	}
}
