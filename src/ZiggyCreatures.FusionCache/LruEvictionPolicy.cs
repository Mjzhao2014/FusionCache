using System;
using System.Collections.Generic;
using ZiggyCreatures.Caching.Fusion.Internals;

namespace ZiggyCreatures.Caching.Fusion;

/// <summary>
/// An eviction policy that removes the least-recently-used entries when configured usage thresholds are exceeded.
/// Accesses and updates are tracked using an internal linked list of keys, so that get/set operations can update
/// recency in O(1) time.
/// </summary>
public class LruEvictionPolicy : IFusionCacheEvictionPolicy
{
    private readonly LinkedList<string> _lruList = new();
    private readonly Dictionary<string, LinkedListNode<string>> _nodes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, long> _sizes = new(StringComparer.Ordinal);
    private readonly object _lock = new();
    private int _entryCount;
    private long _totalSize;

    /// <summary>
    /// Initializes a new LRU eviction policy with the specified configuration.
    /// </summary>
    /// <param name="config">Policy configuration.</param>
    public LruEvictionPolicy(FusionCacheEvictionPolicyConfig config)
    {
        Config = config ?? throw new ArgumentNullException(nameof(config));
    }

    /// <inheritdoc />
    public string Name => "LRU";

    /// <inheritdoc />
    public FusionCacheEvictionPolicyConfig Config { get; }

    /// <inheritdoc />
    public void OnGet(string key)
    {
        lock (_lock)
        {
            if (_nodes.TryGetValue(key, out var node))
            {
                // Move accessed node to the front
                _lruList.Remove(node);
                _lruList.AddFirst(node);
            }
        }
    }

    /// <inheritdoc />
    public void OnSet(string key, FusionCacheEntryMetadata? metadata)
    {
        var size = metadata?.Size ?? 0L;
        lock (_lock)
        {
            if (_nodes.TryGetValue(key, out var node))
            {
                // Update existing entry: move to front and adjust size
                _lruList.Remove(node);
                _lruList.AddFirst(node);
                if (_sizes.TryGetValue(key, out var oldSize))
                {
                    _totalSize += size - oldSize;
                    _sizes[key] = size;
                }
            }
            else
            {
                // New entry
                var newNode = _lruList.AddFirst(key);
                _nodes[key] = newNode;
                _entryCount++;
                _sizes[key] = size;
                _totalSize += size;
            }
        }
    }

    /// <inheritdoc />
    public void OnRemove(string key)
    {
        lock (_lock)
        {
            if (_nodes.TryGetValue(key, out var node))
            {
                _lruList.Remove(node);
                _nodes.Remove(key);
                if (_sizes.TryGetValue(key, out var sz))
                {
                    _totalSize -= sz;
                    _sizes.Remove(key);
                }
                _entryCount--;
            }
        }
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
            // Determine how many to evict by configured percentage of current count
            int toEvictCount = (int)Math.Ceiling(_entryCount * Config.EvictionPercentage);
            toEvictCount = Math.Max(Config.MinEvictionBatchSize, toEvictCount);
            toEvictCount = Math.Min(Config.MaxEvictionBatchSize, toEvictCount);
            var node = _lruList.Last;
            long removedSize = 0;
            while (node != null && keys.Count < toEvictCount)
            {
                string k = node.Value;
                keys.Add(k);
                if (_sizes.TryGetValue(k, out var sz))
                    removedSize += sz;
                node = node.Previous;
                if (sizeTrigger && Config.MaxTotalSize.HasValue)
                {
                    if (_totalSize - removedSize <= Config.MaxTotalSize.Value * Config.EvictionThreshold)
                    {
                        break;
                    }
                }
            }
        }
        return keys;
    }
}
