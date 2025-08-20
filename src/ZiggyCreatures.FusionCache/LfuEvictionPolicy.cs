using System;
using System.Collections.Generic;
using System.Linq;
using ZiggyCreatures.Caching.Fusion.Internals;

namespace ZiggyCreatures.Caching.Fusion
{
	/// <summary>
	/// A least frequently used eviction policy with a tie-breaker of recency among entries with the same frequency.
	/// Uses a dictionary of frequency buckets to linked lists of keys plus an index into entries, providing O(1) get/set/remove.
	/// Eviction scans frequency buckets starting from the current minimum frequency to evict the least frequently used (and least recently used within that frequency).
	/// </summary>
	public class LfuEvictionPolicy : IFusionCacheEvictionPolicy
	{
		private sealed class EntryInfo
		{
			public int Frequency { get; set; }
			public LinkedListNode<string> Node { get; set; } = null!;
		}
		private readonly Dictionary<string, EntryInfo> _entries = new(StringComparer.Ordinal);
		private readonly Dictionary<int, LinkedList<string>> _freqLists = new();
		private readonly object _lock = new();
		private int _minFreq;
		public LfuEvictionPolicy(FusionCacheEvictionPolicyConfig config)
		{
			Config = config ?? throw new ArgumentNullException(nameof(config));
		}
		public string Name => "LFU";
		public FusionCacheEvictionPolicyConfig Config { get; }
		public void OnGet(string key)
		{
			lock (_lock)
			{
				if (_entries.TryGetValue(key, out var info))
				{
					IncrementFrequency(key, info);
				}
			}
		}
		public void OnSet(string key, FusionCacheEntryMetadata? metadata)
		{
			lock (_lock)
			{
				if (_entries.TryGetValue(key, out var info))
				{
					// update frequency as through a get since a set also counts as an access
					IncrementFrequency(key, info);
				}
				else
				{
					// new entry at frequency 1
					var list = GetOrCreateFreqList(1);
					var node = list.AddLast(key);
					_entries[key] = new EntryInfo { Frequency = 1, Node = node };
					_minFreq = 1;
				}
			}
		}
		public void OnRemove(string key)
		{
			lock (_lock)
			{
				if (_entries.TryGetValue(key, out var info))
				{
					var list = _freqLists[info.Frequency];
					list.Remove(info.Node);
					if (list.Count == 0)
					{
						_freqLists.Remove(info.Frequency);
					}
					_entries.Remove(key);
				}
			}
		}
		private void IncrementFrequency(string key, EntryInfo info)
		{
			// remove from current freq list
			var list = _freqLists[info.Frequency];
			list.Remove(info.Node);
			if (list.Count == 0)
			{
				_freqLists.Remove(info.Frequency);
				// if we just emptied the list with the current min frequency, bump min
				if (info.Frequency == _minFreq)
				{
					_minFreq = info.Frequency + 1;
				}
			}
			// increment freq and add to new bucket
			int newFreq = info.Frequency + 1;
			var newList = GetOrCreateFreqList(newFreq);
			var newNode = newList.AddLast(key);
			info.Frequency = newFreq;
			info.Node = newNode;
		}
		private LinkedList<string> GetOrCreateFreqList(int freq)
		{
			if (!_freqLists.TryGetValue(freq, out var list))
			{
				list = new LinkedList<string>();
				_freqLists[freq] = list;
			}
			return list;
		}
		public IEnumerable<string> GetKeysToEvict()
		{
			if (Config.MaxEntryCount is null)
				return Array.Empty<string>();
			lock (_lock)
			{
				int count = _entries.Count;
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
					// ensure we have a min freq bucket to pull from
					if (!_freqLists.TryGetValue(_minFreq, out var list) || list.Count == 0)
					{
						// recompute min freq among existing lists
						if (_freqLists.Count == 0)
							break;
						_minFreq = _freqLists.Keys.Min();
						list = _freqLists[_minFreq];
					}
					var node = list.First;
					if (node is null)
					{
						// no node, skip
						_freqLists.Remove(_minFreq);
						continue;
					}
					string k = node.Value;
					list.RemoveFirst();
					if (list.Count == 0)
					{
						_freqLists.Remove(_minFreq);
					}
					_entries.Remove(k);
					keys.Add(k);
				}
				return keys;
			}
		}
	}
}
