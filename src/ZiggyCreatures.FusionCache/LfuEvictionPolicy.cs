using System;
using System.Collections.Generic;
using System.Linq;

namespace ZiggyCreatures.Caching.Fusion;

/// <summary>
/// An LFU (least frequently used) eviction policy: when capacity is exceeded, evict entries
/// with the lowest access count, breaking ties using least recently used order.
/// </summary>
public class LfuEvictionPolicy : IFusionCacheEvictionPolicy
{
    private class Node
    {
        public string Key;
        public int Freq;
        public LinkedListNode<string> ListNode;
    }

    private readonly object _lock = new();
    private readonly Dictionary<string, Node> _nodes = new();
    private readonly Dictionary<int, LinkedList<string>> _freqLists = new();
    private int _minFreq = 0;

    /// <summary>
    /// Creates a new LFU policy with the specified configuration.
    /// </summary>
    public LfuEvictionPolicy(FusionCacheEvictionPolicyConfig config)
    {
        Config = config ?? throw new ArgumentNullException(nameof(config));
    }

    /// <inheritdoc/>
    public string Name => "LFU";

    /// <inheritdoc/>
    public FusionCacheEvictionPolicyConfig Config { get; }

    /// <inheritdoc/>
    public void OnGet(string key)
    {
        if (key is null) return;
        lock (_lock)
        {
            if (_nodes.TryGetValue(key, out var node))
            {
                UpdateFrequency(node);
            }
        }
    }

    /// <inheritdoc/>
    public void OnSet(string key, Internals.FusionCacheEntryMetadata? metadata)
    {
        if (key is null) return;
        lock (_lock)
        {
            if (_nodes.TryGetValue(key, out var node))
            {
                UpdateFrequency(node);
            }
            else
            {
                var freq = 1;
                if (!_freqLists.TryGetValue(freq, out var list))
                {
                    list = new LinkedList<string>();
                    _freqLists[freq] = list;
                }
                var listNode = list.AddFirst(key);
                _nodes[key] = new Node { Key = key, Freq = freq, ListNode = listNode };
                _minFreq = 1;
            }
        }
    }

    /// <inheritdoc/>
    public void OnRemove(string key)
    {
        if (key is null) return;
        lock (_lock)
        {
            if (_nodes.TryGetValue(key, out var node))
            {
                RemoveNode(node);
            }
        }
    }

    /// <inheritdoc/>
    public IEnumerable<string> GetKeysToEvict()
    {
        if (Config?.MaxEntryCount is null)
            yield break;

        lock (_lock)
        {
            var maxEntryCount = Config.MaxEntryCount.Value;
            var thresholdCount = (int)Math.Floor(maxEntryCount * Config.EvictionThreshold);
            if (thresholdCount <= 0)
                thresholdCount = maxEntryCount;
            if (_nodes.Count <= thresholdCount)
                yield break;

            var batchSize = (int)Math.Ceiling(maxEntryCount * Config.EvictionPercentage);
            if (batchSize < Config.MinEvictionBatchSize)
                batchSize = Config.MinEvictionBatchSize;
            if (batchSize > Config.MaxEvictionBatchSize)
                batchSize = Config.MaxEvictionBatchSize;
            var toEvict = Math.Min(batchSize, _nodes.Count);
            var freq = _minFreq;
            var remaining = toEvict;
            while (remaining > 0 && _freqLists.Count > 0)
            {
                if (!_freqLists.TryGetValue(freq, out var list) || list.Count == 0)
                {
                    freq++;
                    continue;
                }
                var node = list.Last;
                while (node != null && remaining > 0)
                {
                    yield return node.Value;
                    node = node.Previous;
                    remaining--;
                }
                freq++;
            }
        }
    }

    private void UpdateFrequency(Node node)
    {
        // remove from old freq list
        var oldFreq = node.Freq;
        var oldList = _freqLists[oldFreq];
        oldList.Remove(node.ListNode);
        if (oldList.Count == 0)
        {
            _freqLists.Remove(oldFreq);
            if (_minFreq == oldFreq)
                _minFreq = oldFreq + 1;
        }
        // add to new freq list
        var newFreq = oldFreq + 1;
        if (!_freqLists.TryGetValue(newFreq, out var newList))
        {
            newList = new LinkedList<string>();
            _freqLists[newFreq] = newList;
        }
        node.ListNode = newList.AddFirst(node.Key);
        node.Freq = newFreq;
    }

    private void RemoveNode(Node node)
    {
        var freqList = _freqLists[node.Freq];
        freqList.Remove(node.ListNode);
        if (freqList.Count == 0)
        {
            _freqLists.Remove(node.Freq);
            if (_minFreq == node.Freq)
            {
                _minFreq = _freqLists.Keys.Count > 0 ? _freqLists.Keys.Min() : 0;
            }
        }
        _nodes.Remove(node.Key);
    }
}
