using System;
using System.Collections.Generic;

namespace ZiggyCreatures.Caching.Fusion;

/// <summary>
/// An LRU (least recently used) eviction policy: when capacity is exceeded, evict keys that have not been
/// accessed recently. Access is defined as either a get or a set.
/// </summary>
public class LruEvictionPolicy : IFusionCacheEvictionPolicy
{
    private readonly object _lock = new();
    private readonly LinkedList<string> _usageList = new();
    private readonly Dictionary<string, LinkedListNode<string>> _nodes = new();

    /// <summary>
    /// Creates a new LRU policy using the given configuration.
    /// </summary>
    public LruEvictionPolicy(FusionCacheEvictionPolicyConfig config)
    {
        Config = config ?? throw new ArgumentNullException(nameof(config));
    }

    /// <inheritdoc/>
    public string Name => "LRU";

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
                _usageList.Remove(node);
                _usageList.AddFirst(node);
            }
        }
    }

    /// <inheritdoc/>
    public void OnSet(string key, Internals.FusionCacheEntryMetadata? metadata)
    {
        if (key is null) return;
        lock (_lock)
        {
            if (_nodes.TryGetValue(key, out var existing))
            {
                _usageList.Remove(existing);
                _usageList.AddFirst(existing);
            }
            else
            {
                var node = new LinkedListNode<string>(key);
                _usageList.AddFirst(node);
                _nodes[key] = node;
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
                _usageList.Remove(node);
                _nodes.Remove(key);
            }
        }
    }

    /// <inheritdoc/>
    public IEnumerable<string> GetKeysToEvict()
    {
        // if no capacity configured, nothing to evict
        if (Config?.MaxEntryCount is null)
            yield break;

        lock (_lock)
        {
            var maxEntryCount = Config.MaxEntryCount.Value;
            // compute threshold and batch size
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
            var node = _usageList.Last;
            while (node != null && toEvict > 0)
            {
                yield return node.Value;
                node = node.Previous;
                toEvict--;
            }
        }
    }
}
