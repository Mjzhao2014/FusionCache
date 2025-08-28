using System;
using System.Collections.Generic;
using ZiggyCreatures.Caching.Fusion.Internals;

namespace ZiggyCreatures.Caching.Fusion;

/// <summary>
/// A Least Frequently Used eviction policy: when capacity is exceeded, entries with the lowest access frequency
/// are evicted first, and ties are broken by least recently used order within the same frequency.
/// </summary>
public class LfuEvictionPolicy
    : IFusionCacheEvictionPolicy
{
    private class ItemInfo
    {
        public int Frequency;
        public LinkedListNode<string> Node;
    }

    private readonly Dictionary<string, ItemInfo> _map = new();
    private readonly Dictionary<int, LinkedList<string>> _freqLists = new();
    private int _minFreq;
    private readonly object _lock = new();

    public LfuEvictionPolicy(FusionCacheEvictionPolicyConfig config)
    {
        Config = config ?? throw new ArgumentNullException(nameof(config));
    }

    public LfuEvictionPolicy(int maxEntryCount, double evictionPercentage = 0.1)
        : this(new FusionCacheEvictionPolicyConfig { MaxEntryCount = maxEntryCount, EvictionPercentage = evictionPercentage })
    { }

    /// <inheritdoc />
    public string Name => "LFU";

    /// <inheritdoc />
    public FusionCacheEvictionPolicyConfig Config { get; }

    /// <inheritdoc />
    public void OnGet(string key)
    {
        lock (_lock)
        {
            if (_map.TryGetValue(key, out var info))
            {
                UpdateFrequency(info);
            }
        }
    }

    /// <inheritdoc />
    public void OnSet(string key, FusionCacheEntryMetadata? metadata)
    {
        lock (_lock)
        {
            if (_map.TryGetValue(key, out var info))
            {
                // existing item behaves like an access
                UpdateFrequency(info);
            }
            else
            {
                // new item frequency = 1
                var list = GetListForFrequency(1);
                var node = list.AddLast(key);
                _map[key] = new ItemInfo { Frequency = 1, Node = node };
                _minFreq = 1;
            }
        }
    }

    /// <inheritdoc />
    public void OnRemove(string key)
    {
        lock (_lock)
        {
            if (_map.TryGetValue(key, out var info))
            {
                RemoveNode(info);
            }
        }
    }

    /// <inheritdoc />
    public IEnumerable<string> GetKeysToEvict()
    {
        var result = new List<string>();
        var capacity = Config.MaxEntryCount;
        if (capacity is null)
            return result;

        lock (_lock)
        {
            var thresholdCount = (int)Math.Ceiling(capacity.Value * Config.EvictionThreshold);
            if (_map.Count <= thresholdCount)
                return result;

            var batchToRemove = (int)Math.Ceiling(capacity.Value * Config.EvictionPercentage);
            batchToRemove = Math.Max(batchToRemove, Config.MinEvictionBatchSize);
            batchToRemove = Math.Min(batchToRemove, Config.MaxEvictionBatchSize);
            batchToRemove = Math.Min(batchToRemove, _map.Count);

            while (batchToRemove > 0)
            {
                if (!_freqLists.TryGetValue(_minFreq, out var list) || list.Count == 0)
                {
                    // find next min freq
                    var nextMin = _minFreq + 1;
                    while (nextMin <= int.MaxValue)
                    {
                        if (_freqLists.TryGetValue(nextMin, out var nextList) && nextList.Count > 0)
                        {
                            _minFreq = nextMin;
                            list = nextList;
                            break;
                        }
                        nextMin++;
                    }
                    if (list == null || list.Count == 0)
                        break;
                }
                var node = list.First;
                if (node is null)
                    break;
                list.RemoveFirst();
                _map.Remove(node.Value);
                result.Add(node.Value);
                batchToRemove--;
                if (list.Count == 0)
                {
                    _freqLists.Remove(_minFreq);
                    // next iteration will adjust _minFreq if necessary
                }
            }
        }
        return result;
    }

    private void UpdateFrequency(ItemInfo info)
    {
        var oldFreq = info.Frequency;
        if (_freqLists.TryGetValue(oldFreq, out var list))
        {
            list.Remove(info.Node);
            if (list.Count == 0)
            {
                _freqLists.Remove(oldFreq);
                if (_minFreq == oldFreq)
                {
                    _minFreq = oldFreq + 1;
                }
            }
        }
        var newFreq = oldFreq + 1;
        var newList = GetListForFrequency(newFreq);
        var newNode = newList.AddLast(info.Node.Value);
        info.Frequency = newFreq;
        info.Node = newNode;
    }

    private LinkedList<string> GetListForFrequency(int freq)
    {
        if (!_freqLists.TryGetValue(freq, out var list))
        {
            list = new LinkedList<string>();
            _freqLists[freq] = list;
        }
        return list;
    }

    private void RemoveNode(ItemInfo info)
    {
        if (_freqLists.TryGetValue(info.Frequency, out var list))
        {
            list.Remove(info.Node);
            if (list.Count == 0)
            {
                _freqLists.Remove(info.Frequency);
                if (_minFreq == info.Frequency)
                {
                    // Min frequency list became empty: min may increase, but will be recalculated lazily if items remain.
                    _minFreq++;
                }
            }
        }
        _map.Remove(info.Node.Value);
    }
}
