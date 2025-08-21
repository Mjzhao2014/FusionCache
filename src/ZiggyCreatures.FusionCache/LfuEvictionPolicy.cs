using System.Collections.Generic;
using ZiggyCreatures.Caching.Fusion.Internals;

namespace ZiggyCreatures.Caching.Fusion;

/// <summary>
/// Implements a least-frequently-used eviction policy: entries with the lowest
/// access counts will be evicted first, with ties broken by recency within the same frequency.
/// </summary>
public class LfuEvictionPolicy
	: IFusionCacheEvictionPolicy
{
	private sealed class Entry
	{
		public int Frequency;
		public LinkedListNode<string> Node;
	}

	private readonly Dictionary<string, Entry> _entries = new();
	private readonly Dictionary<int, LinkedList<string>> _freqLists = new();
	private int _minFreq;
	private readonly object _lock = new();

	/// <summary>
	/// Instantiates a new LFU eviction policy using the provided configuration.
	/// </summary>
	public LfuEvictionPolicy(FusionCacheEvictionPolicyConfig config)
	{
		Config = config;
	}

	/// <inheritdoc/>
	public string Name => "LFU";

	/// <inheritdoc/>
	public FusionCacheEvictionPolicyConfig Config { get; }

	/// <inheritdoc/>
	public void OnGet(string key)
	{
		lock (_lock)
		{
			if (_entries.TryGetValue(key, out var entry))
			{
				IncreaseFrequency(entry);
			}
		}
	}

	/// <inheritdoc/>
	public void OnSet(string key, FusionCacheEntryMetadata? metadata)
	{
		lock (_lock)
		{
			if (_entries.TryGetValue(key, out var entry))
			{
				IncreaseFrequency(entry);
			}
			else
			{
				var freq = 1;
				if (!_freqLists.TryGetValue(freq, out var list))
				{
					list = new LinkedList<string>();
					_freqLists[freq] = list;
				}
				var node = list.AddFirst(key);
				_entries[key] = new Entry { Frequency = freq, Node = node };
				_minFreq = 1;
			}
		}
	}

	private void IncreaseFrequency(Entry entry)
	{
		var currentFreq = entry.Frequency;
		if (_freqLists.TryGetValue(currentFreq, out var list))
		{
			list.Remove(entry.Node);
			if (list.Count == 0)
			{
				_freqLists.Remove(currentFreq);
				if (_minFreq == currentFreq)
				{
					_minFreq++;
				}
			}
		}
		var newFreq = currentFreq + 1;
		if (!_freqLists.TryGetValue(newFreq, out var newList))
		{
			newList = new LinkedList<string>();
			_freqLists[newFreq] = newList;
		}
		var newNode = newList.AddFirst(entry.Node.Value);
		entry.Frequency = newFreq;
		entry.Node = newNode;
	}

	/// <inheritdoc/>
	public void OnRemove(string key)
	{
		lock (_lock)
		{
			if (_entries.TryGetValue(key, out var entry))
			{
				if (_freqLists.TryGetValue(entry.Frequency, out var list))
				{
					list.Remove(entry.Node);
					if (list.Count == 0)
					{
						_freqLists.Remove(entry.Frequency);
						if (_minFreq == entry.Frequency)
						{
							// find next min freq if any lists remain
							if (_freqLists.Count > 0)
							{
								_minFreq = int.MaxValue;
								foreach (var freq in _freqLists.Keys)
								{
									if (freq < _minFreq)
										_minFreq = freq;
								}
							}
							else
							{
								_minFreq = 0;
							}
						}
					}
				}
				_entries.Remove(key);
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
			var count = _entries.Count;
			var max = Config.MaxEntryCount.Value;
			var triggerCount = (int)Math.Floor(max * Config.EvictionThreshold);
			if (count <= triggerCount)
				return Array.Empty<string>();

			var batch = (int)Math.Ceiling(max * Config.EvictionPercentage);
			batch = Math.Max(batch, Config.MinEvictionBatchSize);
			batch = Math.Min(batch, Config.MaxEvictionBatchSize);

			var result = new List<string>(batch);
			var freq = _minFreq;
			while (result.Count < batch && _freqLists.Count > 0)
			{
				if (!_freqLists.TryGetValue(freq, out var list) || list.Count == 0)
				{
					freq++;
					continue;
				}
				var node = list.Last;
				while (node is not null && result.Count < batch)
				{
					result.Add(node.Value);
					node = node.Previous;
				}
				if (result.Count < batch)
				{
					freq++;
				}
			}
			return result;
		}
	}
}
