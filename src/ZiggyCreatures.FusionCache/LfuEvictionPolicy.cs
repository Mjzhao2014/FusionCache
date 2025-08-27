using System;
using System.Collections.Generic;
using ZiggyCreatures.Caching.Fusion.Internals;

namespace ZiggyCreatures.Caching.Fusion;

/// <summary>
/// Eviction policy based on Least Frequently Used semantics, evicting items with the
/// lowest access count, and using recency as a tie-breaker within the same frequency.
/// Maintains O(1) updates using a frequency→list-of-keys structure.
/// </summary>
public sealed class LfuEvictionPolicy : IFusionCacheEvictionPolicy
{
    private class Node
    {
        public string Key { get; }
        public int Frequency { get; set; }
        public LinkedListNode<Node> ListNode { get; set; }
        public Node(string key, int freq)
        {
            Key = key;
            Frequency = freq;
        }
    }

    // map key → node which holds frequency and pointer into frequency list
    private readonly Dictionary<string, Node> _nodes = new(StringComparer.Ordinal);
    // map frequency → list of nodes in recency order (most recently used at head)
    private readonly Dictionary<int, LinkedList<Node>> _freqLists = new();
    private int _minFreq = 0;
    private readonly object _mutex = new();

    /// <summary>
    /// Creates a new LFU eviction policy instance with the given configuration.
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
        if (key is null)
            throw new ArgumentNullException(nameof(key));
        lock (_mutex)
        {
            if (_nodes.TryGetValue(key, out var node))
            {
                IncrementFrequency(node);
            }
        }
    }

    /// <inheritdoc/>
    public void OnSet(string key, FusionCacheEntryMetadata? metadata)
    {
        if (key is null)
            throw new ArgumentNullException(nameof(key));
        lock (_mutex)
        {
            if (_nodes.TryGetValue(key, out var existing))
            {
                // count set as an access as well
                IncrementFrequency(existing);
            }
            else
            {
                var node = new Node(key, 1);
                var list = GetOrCreateList(1);
                node.ListNode = list.AddFirst(node);
                _nodes[key] = node;
                _minFreq = 1;
            }
        }
    }

    /// <inheritdoc/>
    public void OnRemove(string key)
    {
        if (key is null)
            throw new ArgumentNullException(nameof(key));
        lock (_mutex)
        {
            if (_nodes.TryGetValue(key, out var node))
            {
                RemoveNode(node);
            }
        }
    }

    private void IncrementFrequency(Node node)
    {
        var oldFreq = node.Frequency;
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
        node.Frequency++;
        var newList = GetOrCreateList(node.Frequency);
        node.ListNode = newList.AddFirst(node);
    }

    private LinkedList<Node> GetOrCreateList(int freq)
    {
        if (_freqLists.TryGetValue(freq, out var list))
            return list;
        list = new LinkedList<Node>();
        _freqLists[freq] = list;
        return list;
    }

    private void RemoveNode(Node node)
    {
        var freq = node.Frequency;
        _nodes.Remove(node.Key);
        var list = _freqLists[freq];
        list.Remove(node.ListNode);
        if (list.Count == 0)
        {
            _freqLists.Remove(freq);
            if (_minFreq == freq)
            {
                // min freq might increase, find next existing
                if (_freqLists.Count > 0)
                {
                    _minFreq = int.MaxValue;
                    foreach (var f in _freqLists.Keys)
                    {
                        if (f < _minFreq)
                            _minFreq = f;
                    }
                }
                else
                {
                    _minFreq = 0;
                }
            }
        }
    }

    /// <inheritdoc/>
    public IEnumerable<string> GetKeysToEvict()
    {
        if (Config.MaxEntryCount is null)
            return Array.Empty<string>();
        List<string> keys = new();
        lock (_mutex)
        {
            var max = Config.MaxEntryCount.Value;
            if (max <= 0)
                return Array.Empty<string>();
            var thresholdCount = (int)Math.Floor(max * Config.EvictionThreshold);
            if (_nodes.Count <= thresholdCount)
                return Array.Empty<string>();
            var toEvict = (int)Math.Ceiling(max * Config.EvictionPercentage);
            toEvict = Math.Max(toEvict, Config.MinEvictionBatchSize);
            toEvict = Math.Min(toEvict, Config.MaxEvictionBatchSize);
            toEvict = Math.Min(toEvict, _nodes.Count);
            for (int i = 0; i < toEvict; i++)
            {
                if (_minFreq == 0)
                    break;
                if (!_freqLists.TryGetValue(_minFreq, out var list) || list.Count == 0)
                {
                    // find next non-empty frequency list
                    if (_freqLists.Count == 0)
                        break;
                    _minFreq = int.MaxValue;
                    foreach (var f in _freqLists.Keys)
                    {
                        if (f < _minFreq)
                            _minFreq = f;
                    }
                    list = _freqLists[_minFreq];
                }
                var node = list.Last;
                if (node == null)
                    break;
                list.RemoveLast();
                _nodes.Remove(node.Value.Key);
                keys.Add(node.Value.Key);
                // adjust _minFreq and cleanup list if needed
                if (list.Count == 0)
                {
                    _freqLists.Remove(node.Value.Frequency);
                    // min freq will be recomputed in loop if needed
                }
            }
            // recompute _minFreq if needed
            if (_nodes.Count == 0)
            {
                _minFreq = 0;
            }
            else if (_freqLists.Count > 0)
            {
                // find the lowest existing frequency as new min
                _minFreq = int.MaxValue;
                foreach (var f in _freqLists.Keys)
                {
                    if (f < _minFreq)
                        _minFreq = f;
                }
            }
        }
        return keys;
    }
}
