using System;
using System.Collections.Generic;
using ZiggyCreatures.Caching.Fusion.Internals;

namespace ZiggyCreatures.Caching.Fusion
{
	/// <summary>
	/// Least Frequently Used eviction policy implementation: tracks usage counts per entry and removes the lowest frequency entries when capacity is reached.
	/// </summary>
	public class LfuEvictionPolicy : IFusionCacheEvictionPolicy
	{
		private sealed class ItemInfo
		{
			public int Freq;
			public LinkedListNode<string> Node;
		}
		private readonly object _lock = new();
		private readonly Dictionary<string, ItemInfo> _items = new();
		private readonly Dictionary<int, LinkedList<string>> _freqLists = new();
		private int _minFreq = 1;
		private int _count;
		public string Name => "LFU";
		public FusionCacheEvictionPolicyConfig Config { get; }
		public LfuEvictionPolicy(FusionCacheEvictionPolicyConfig config)
		{
			Config = config ?? throw new ArgumentNullException(nameof(config));
		}
		public void OnGet(string key)
		{
			if (key is null)
				return;
			lock (_lock)
			{
				if (_items.TryGetValue(key, out var info))
				{
					UpdateFrequency(info);
				}
			}
		}
		public void OnSet(string key, FusionCacheEntryMetadata? metadata)
		{
			if (key is null)
				return;
			lock (_lock)
			{
				if (_items.TryGetValue(key, out var info))
				{
					// treat set as usage: increment freq
					UpdateFrequency(info);
				}
				else
				{
					// new item
					var list = GetList(1);
					var node = list.AddLast(key);
					info = new ItemInfo { Freq = 1, Node = node };
					_items[key] = info;
					_minFreq = 1;
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
				if (_items.TryGetValue(key, out var info))
				{
					var freq = info.Freq;
					var list = _freqLists[freq];
					list.Remove(info.Node);
					_items.Remove(key);
					_count--;
					if (list.Count == 0)
					{
						_freqLists.Remove(freq);
						if (_minFreq == freq)
						{
							// recalc min freq
							UpdateMinFreq();
						}
					}
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
				evictCount = Math.Min(evictCount, _count);
				while (keys.Count < evictCount && _freqLists.Count > 0)
				{
					if (_freqLists.TryGetValue(_minFreq, out var list) == false || list.Count == 0)
					{
						UpdateMinFreq();
						continue;
					}
					var node = list.First;
					if (node is null)
					{
						UpdateMinFreq();
						continue;
					}
					var key = node.Value;
					list.RemoveFirst();
					_items.Remove(key);
					_count--;
					keys.Add(key);
					if (list.Count == 0)
					{
						_freqLists.Remove(_minFreq);
						UpdateMinFreq();
					}
				}
			}
			return keys;
		}
		private void UpdateFrequency(ItemInfo info)
		{
			// remove from current freq bucket
			var oldFreq = info.Freq;
			var oldList = _freqLists[oldFreq];
			oldList.Remove(info.Node);
			if (oldList.Count == 0)
			{
				_freqLists.Remove(oldFreq);
				if (_minFreq == oldFreq)
				{
					_minFreq = oldFreq + 1;
				}
			}
			// add to next freq bucket
			var newFreq = oldFreq + 1;
			var newList = GetList(newFreq);
			var newNode = newList.AddLast(info.Node.Value);
			info.Freq = newFreq;
			info.Node = newNode;
		}
		private LinkedList<string> GetList(int freq)
		{
			if (!_freqLists.TryGetValue(freq, out var list))
			{
				list = new LinkedList<string>();
				_freqLists[freq] = list;
			}
			return list;
		}
		private void UpdateMinFreq()
		{
			if (_freqLists.Count == 0)
			{
				_minFreq = 1;
			}
			else
			{
				// pick the lowest frequency present
				_minFreq = int.MaxValue;
				foreach (var freq in _freqLists.Keys)
				{
					if (freq < _minFreq)
					{
						_minFreq = freq;
					}
				}
			}
		}
	}
}
