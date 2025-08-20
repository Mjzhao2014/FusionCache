using System;
using System.Collections.Generic;
using ZiggyCreatures.Caching.Fusion.Internals;

namespace ZiggyCreatures.Caching.Fusion;

/// <summary>
/// An eviction policy that removes the least-frequently-used entries when configured usage thresholds are exceeded.
/// Access counts are tracked per key in O(1) time using frequency buckets.
/// </summary>
public class LfuEvictionPolicy : IFusionCacheEvictionPolicy
{
	private class EntryInfo
	{
		public string Key = string.Empty;
		public int Frequency;
		public LinkedListNode<string>? Node;
		public long Size;
	}

    private readonly Dictionary<string, EntryInfo> _entries = new(StringComparer.Ordinal);
    private readonly Dictionary<int, LinkedList<string>> _freqLists = new();
    private int _minFreq = 1;
    private readonly object _lock = new();
    private int _entryCount;
    private long _totalSize;

    /// <summary>
    /// Initializes a new LFU eviction policy with the specified configuration.
    /// </summary>
    /// <param name="config">Policy configuration.</param>
    public LfuEvictionPolicy(FusionCacheEvictionPolicyConfig config)
    {
        Config = config ?? throw new ArgumentNullException(nameof(config));
    }

    /// <inheritdoc />
    public string Name => "LFU";

    /// <inheritdoc />
    public FusionCacheEvictionPolicyConfig Config { get; }

    /// <inheritdoc />
    public void OnGet(string key)
    {
        lock (_lock)
        {
            if (!_entries.TryGetValue(key, out var e))
                return;
            UpdateFrequency(e);
        }
    }

    /// <inheritdoc />
    public void OnSet(string key, FusionCacheEntryMetadata? metadata)
    {
        var size = metadata?.Size ?? 0L;
        lock (_lock)
        {
            if (_entries.TryGetValue(key, out var e))
            {
                // treat set as an access: update size and bump freq
                var delta = size - e.Size;
                e.Size = size;
                _totalSize += delta;
                UpdateFrequency(e);
            }
            else
            {
                // new entry at freq=1
                var list = GetFreqList(1);
				var node = list.AddLast(key);
				var info = new EntryInfo { Key = key, Frequency = 1, Node = node, Size = size };
                _entries[key] = info;
                _minFreq = 1;
                _entryCount++;
                _totalSize += size;
            }
        }
    }

    /// <inheritdoc />
    public void OnRemove(string key)
    {
        lock (_lock)
        {
            if (_entries.TryGetValue(key, out var e))
            {
                RemoveEntryInternal(e);
            }
        }
    }

    private void UpdateFrequency(EntryInfo e)
    {
        var oldFreq = e.Frequency;
			var oldList = _freqLists[oldFreq];
			if (e.Node is null)
				return;
			oldList.Remove(e.Node);
        if (oldList.Count == 0)
        {
            _freqLists.Remove(oldFreq);
            if (_minFreq == oldFreq)
            {
                _minFreq = oldFreq + 1;
            }
        }
        var newFreq = oldFreq + 1;
			var list = GetFreqList(newFreq);
			// if we had a previous linked list node, reuse its value, otherwise use the stored key
			var key = e.Node?.Value ?? e.Key;
			e.Node = list.AddLast(key);
			e.Frequency = newFreq;
    }

    private LinkedList<string> GetFreqList(int freq)
    {
        if (!_freqLists.TryGetValue(freq, out var list))
        {
            list = new LinkedList<string>();
            _freqLists[freq] = list;
        }
        return list;
    }

    private void RemoveEntryInternal(EntryInfo e)
    {
        var freq = e.Frequency;
		var list = _freqLists[freq];
		if (e.Node is not null)
		{
			list.Remove(e.Node);
		}
        if (list.Count == 0)
        {
            _freqLists.Remove(freq);
            if (_minFreq == freq)
            {
                // adjust min freq upward to next existing bucket or 1 if none
                _minFreq = 1;
                if (_freqLists.Count > 0)
                {
                    // find next min
                    var min = int.MaxValue;
                    foreach (var f in _freqLists.Keys)
                    {
                        if (f < min)
                            min = f;
                    }
                    _minFreq = min;
                }
            }
        }
		_entries.Remove(e.Key);
        _entryCount--;
        _totalSize -= e.Size;
    }

    /// <inheritdoc />
    public IEnumerable<string> GetKeysToEvict()
    {
        List<string> keys = new();
        lock (_lock)
        {
            bool countTrigger = Config.MaxEntryCount.HasValue && _entryCount > (int)(Config.MaxEntryCount.Value * Config.EvictionThreshold);
            bool sizeTrigger = Config.MaxTotalSize.HasValue && _totalSize > (long)(Config.MaxTotalSize.Value * Config.EvictionThreshold);
            if (!countTrigger && !sizeTrigger)
                return keys;
            int toEvictCount = (int)Math.Ceiling(_entryCount * Config.EvictionPercentage);
            toEvictCount = Math.Max(Config.MinEvictionBatchSize, toEvictCount);
            toEvictCount = Math.Min(Config.MaxEvictionBatchSize, toEvictCount);
            long removedSize = 0;
            int removedCount = 0;
            int freq = _minFreq;
            while (removedCount < toEvictCount && _freqLists.Count > 0)
            {
                if (!_freqLists.TryGetValue(freq, out var list) || list.Count == 0)
                {
                    freq++;
                    continue;
                }
                var node = list.First;
                while (node != null && removedCount < toEvictCount)
                {
                    var key = node.Value;
                    keys.Add(key);
                    if (_entries.TryGetValue(key, out var info))
                    {
                        removedSize += info.Size;
                    }
                    removedCount++;
                    node = node.Next;
                    if (sizeTrigger && Config.MaxTotalSize.HasValue)
                    {
                        if (_totalSize - removedSize <= Config.MaxTotalSize.Value * Config.EvictionThreshold)
                        {
                            break;
                        }
                    }
                }
                // break out of outer loop if size threshold satisfied as well
                if (sizeTrigger && Config.MaxTotalSize.HasValue && _totalSize - removedSize <= Config.MaxTotalSize.Value * Config.EvictionThreshold)
                {
                    break;
                }
                freq++;
            }
        }
        return keys;
    }
}
