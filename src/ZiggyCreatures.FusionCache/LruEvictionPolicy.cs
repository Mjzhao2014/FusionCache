using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using ZiggyCreatures.Caching.Fusion.Internals;

namespace ZiggyCreatures.Caching.Fusion;

/// <summary>
/// Eviction policy based on Least Recently Used semantics. Tracks recency of access
/// and when capacity thresholds are exceeded returns the least recently used items
/// for eviction.
/// </summary>
public sealed class LruEvictionPolicy : IFusionCacheEvictionPolicy
{
    private readonly LinkedList<string> _lruList = new();
    private readonly Dictionary<string, LinkedListNode<string>> _nodes = new(StringComparer.Ordinal);
    private readonly object _mutex = new();

    /// <summary>
    /// Creates a new LRU eviction policy instance with the given configuration.
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
        if (key is null)
            throw new ArgumentNullException(nameof(key));
        lock (_mutex)
        {
            if (_nodes.TryGetValue(key, out var node))
            {
                // move to front (most recent)
                _lruList.Remove(node);
                _lruList.AddFirst(node);
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
                // move to front
                _lruList.Remove(existing);
                _lruList.AddFirst(existing);
            }
            else
            {
                var node = new LinkedListNode<string>(key);
                _lruList.AddFirst(node);
                _nodes[key] = node;
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
                _lruList.Remove(node);
                _nodes.Remove(key);
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
            // ensure we don't try to evict more than current count
            toEvict = Math.Min(toEvict, _nodes.Count);
            for (int i = 0; i < toEvict; i++)
            {
                var node = _lruList.Last;
                if (node == null)
                    break;
                _lruList.RemoveLast();
                _nodes.Remove(node.Value);
                keys.Add(node.Value);
            }
        }
        return keys;
    }
}
