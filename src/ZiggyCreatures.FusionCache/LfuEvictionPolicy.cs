using System;
using System.Collections.Generic;
using System.Linq;
using ZiggyCreatures.Caching.Fusion.Internals;

namespace ZiggyCreatures.Caching.Fusion
{
	/// <summary>
	/// An eviction policy that removes entries with the lowest access frequency
	/// when capacity limits are reached (Least Frequently Used). Maintains per-frequency
	/// doubly linked lists of nodes to support O(1) updates on get/set.
	/// </summary>
	public class LfuEvictionPolicy : IFusionCacheEvictionPolicy
	{
		private readonly object _lock = new();
		private readonly Dictionary<string, Node> _nodes = new();
		private readonly Dictionary<int, FreqList> _freqLists = new();
		private int _minFreq;
		private int _count;
		private long _totalSize;
		public LfuEvictionPolicy(FusionCacheEvictionPolicyConfig config)
		{
			Config = config ?? throw new ArgumentNullException(nameof(config));
		}
		public string Name => "LFU";
		public FusionCacheEvictionPolicyConfig Config { get; }
		private sealed class Node
		{
			public string Key;
			public int Freq;
			public long Size;
			public Node? Prev;
			public Node? Next;
			public Node(string key, long size)
			{
				Key = key;
				Size = size;
				Freq = 1;
			}
		}
		private sealed class FreqList
		{
			public Node? Head;
			public Node? Tail;
		}
		private static long GetEntrySize(FusionCacheEntryMetadata? metadata) => metadata?.Size ?? 1;
		private FreqList GetOrCreateFreqList(int freq)
		{
			if (!_freqLists.TryGetValue(freq, out var list))
			{
				list = new FreqList();
				_freqLists[freq] = list;
			}
			return list;
		}
		private void AddNodeToFreqList(Node node, int freq)
		{
			var list = GetOrCreateFreqList(freq);
			node.Prev = null;
			node.Next = list.Head;
			if (list.Head != null)
			{
				list.Head.Prev = node;
			}
			list.Head = node;
			if (list.Tail == null)
			{
				list.Tail = node;
			}
		}
		private void RemoveNodeFromFreqList(Node node, int freq)
		{
			if (!_freqLists.TryGetValue(freq, out var list))
				return;
			if (node.Prev != null)
			{
				node.Prev.Next = node.Next;
			}
			else
			{
				list.Head = node.Next;
			}
			if (node.Next != null)
			{
				node.Next.Prev = node.Prev;
			}
			else
			{
				list.Tail = node.Prev;
			}
			if (list.Head == null)
			{
				_freqLists.Remove(freq);
				if (_minFreq == freq)
				{
					_minFreq = freq + 1;
				}
			}
			node.Prev = null;
			node.Next = null;
		}
		private void IncrementFrequency(Node node)
		{
			var curFreq = node.Freq;
			RemoveNodeFromFreqList(node, curFreq);
			node.Freq = curFreq + 1;
			AddNodeToFreqList(node, node.Freq);
			if (!_freqLists.ContainsKey(_minFreq))
			{
				_minFreq = node.Freq;
			}
		}
		public void OnGet(string key)
		{
			lock (_lock)
			{
				if (_nodes.TryGetValue(key, out var node))
				{
					IncrementFrequency(node);
				}
			}
		}
		public void OnSet(string key, FusionCacheEntryMetadata? metadata)
		{
			lock (_lock)
			{
				if (_nodes.TryGetValue(key, out var node))
				{
					var newSize = GetEntrySize(metadata);
					_totalSize += newSize - node.Size;
					node.Size = newSize;
					IncrementFrequency(node);
				}
				else
				{
					var size = GetEntrySize(metadata);
					var newNode = new Node(key, size);
					_nodes[key] = newNode;
					AddNodeToFreqList(newNode, 1);
					_minFreq = 1;
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
					RemoveNodeFromFreqList(node, node.Freq);
					_nodes.Remove(key);
					_count--;
					_totalSize -= node.Size;
					if (_count == 0)
					{
						_minFreq = 0;
					}
				}
			}
		}
		public IEnumerable<string> GetKeysToEvict()
		{
			lock (_lock)
			{
				var result = new List<string>();
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
					return result;
				int removedCount = 0;
				long removedSize = 0;
				int targetRemoveCount = (int)Math.Ceiling(_count * Config.EvictionPercentage);
				targetRemoveCount = Math.Max(targetRemoveCount, Config.MinEvictionBatchSize);
				int maxRemove = Config.MaxEvictionBatchSize;
				foreach (var freq in _freqLists.Keys.OrderBy(f => f))
				{
					var list = _freqLists[freq];
					var node = list.Tail;
					while (node != null && removedCount < maxRemove)
					{
						result.Add(node.Key);
						removedCount++;
						removedSize += node.Size;
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
								return result;
						}
						node = node.Prev;
					}
					if (removedCount >= maxRemove)
						return result;
				}
				return result;
			}
		}
	}
}
