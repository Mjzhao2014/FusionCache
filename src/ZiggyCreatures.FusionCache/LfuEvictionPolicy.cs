using System;
using System.Collections.Generic;
using System.Linq;
using ZiggyCreatures.Caching.Fusion.Internals;

namespace ZiggyCreatures.Caching.Fusion;

/// <summary>
/// Least Frequently Used eviction policy: maintains per-frequency lists of keys.
/// On access, a key's frequency is incremented; when capacity is exceeded, keys with the lowest frequency are evicted first,
/// with a tie-breaker of least recently used among that frequency bucket.
/// </summary>
public class LfuEvictionPolicy : IFusionCacheEvictionPolicy
{
    private class Node
    {
        public int Freq;
        public LinkedListNode<string> ListNode;
    }

    private readonly Dictionary<string, Node> _dict = new Dictionary<string, Node>();
    private readonly Dictionary<int, LinkedList<string>> _freqLists = new Dictionary<int, LinkedList<string>>();
    private int _minFreq = 1;
    private readonly object _lock = new object();

    /// <summary>
    /// Creates a new LFU eviction policy using the specified configuration.
    /// </summary>
    /// <param name="config">The eviction policy configuration.</param>
    public LfuEvictionPolicy(FusionCacheEvictionPolicyConfig config)
    {
        Config = config;
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
            if (!_dict.TryGetValue(key, out var node))
                return;
            Promote(node);
        }
    }

    /// <inheritdoc />
    public void OnSet(string key, FusionCacheEntryMetadata? metadata)
    {
        lock (_lock)
        {
            if (_dict.TryGetValue(key, out var existing))
            {
                Promote(existing);
            }
            else
            {
                // new entry with freq 1
                if (!_freqLists.TryGetValue(1, out var list))
                {
                    list = new LinkedList<string>();
                    _freqLists[1] = list;
                }
                var node = new Node
                {
                    Freq = 1,
                    ListNode = list.AddFirst(key)
                };
                _dict[key] = node;
                _minFreq = 1;
            }
        }
    }

    /// <inheritdoc />
    public void OnRemove(string key)
    {
        lock (_lock)
        {
            if (!_dict.TryGetValue(key, out var node))
                return;
            var freq = node.Freq;
            var list = _freqLists[freq];
            list.Remove(node.ListNode);
            _dict.Remove(key);
            if (list.Count == 0)
            {
                _freqLists.Remove(freq);
                if (_minFreq == freq)
                {
                    // recalculate minFreq
                    if (_freqLists.Count > 0)
                        _minFreq = _freqLists.Keys.Min();
                    else
                        _minFreq = 1;
                }
            }
        }
    }

    /// <inheritdoc />
    public IEnumerable<string> GetKeysToEvict()
    {
        var result = new List<string>();
        if (Config.MaxEntryCount is null)
            return result;
        lock (_lock)
        {
            var max = Config.MaxEntryCount.Value;
            var thresholdCount = (int)Math.Ceiling(max * Config.EvictionThreshold);
            if (_dict.Count < thresholdCount)
                return result;
			var batchSize = (int)Math.Ceiling(max * Config.EvictionPercentage);
			// clamp without Math.Clamp for netstandard2.0
			if (batchSize < Config.MinEvictionBatchSize)
				batchSize = Config.MinEvictionBatchSize;
			if (batchSize > Config.MaxEvictionBatchSize)
				batchSize = Config.MaxEvictionBatchSize;
            int freq = _minFreq;
            while (result.Count < batchSize)
            {
                if (!_freqLists.TryGetValue(freq, out var list) || list.Count == 0)
                {
                    freq++;
                    if (freq > int.MaxValue)
                        break;
                    continue;
                }
                var node = list.Last;
                while (node != null && result.Count < batchSize)
                {
                    result.Add(node.Value);
                    node = node.Previous;
                }
                freq++;
            }
            return result;
        }
    }

    private void Promote(Node node)
    {
        var oldFreq = node.Freq;
        var oldList = _freqLists[oldFreq];
        oldList.Remove(node.ListNode);
        if (oldList.Count == 0)
        {
            _freqLists.Remove(oldFreq);
            if (_minFreq == oldFreq)
            {
                _minFreq = oldFreq + 1;
            }
        }
        var newFreq = oldFreq + 1;
        if (!_freqLists.TryGetValue(newFreq, out var newList))
        {
            newList = new LinkedList<string>();
            _freqLists[newFreq] = newList;
        }
        node.Freq = newFreq;
        node.ListNode = newList.AddFirst(node.ListNode.Value);
    }
}
