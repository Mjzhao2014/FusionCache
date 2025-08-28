using System;
using System.Collections.Generic;
using ZiggyCreatures.Caching.Fusion.Internals;

namespace ZiggyCreatures.Caching.Fusion;

/// <summary>
/// A Least Recently Used eviction policy: when capacity is exceeded, entries that have not been accessed
/// for the longest time are evicted first.
/// </summary>
public class LruEvictionPolicy
    : IFusionCacheEvictionPolicy
{
    private readonly Dictionary<string, LinkedListNode<string>> _map = new();
    private readonly LinkedList<string> _list = new();
    private readonly object _lock = new();

    /// <summary>
    /// Create a new LRU eviction policy with the given configuration.
    /// </summary>
    public LruEvictionPolicy(FusionCacheEvictionPolicyConfig config)
    {
        Config = config ?? throw new ArgumentNullException(nameof(config));
    }

    /// <summary>
    /// Create a new LRU eviction policy specifying only the maximum entry count and optional eviction percentage.
    /// Other configuration values will use defaults.
    /// </summary>
    public LruEvictionPolicy(int maxEntryCount, double evictionPercentage = 0.1)
        : this(new FusionCacheEvictionPolicyConfig { MaxEntryCount = maxEntryCount, EvictionPercentage = evictionPercentage })
    { }

    /// <inheritdoc />
    public string Name => "LRU";

    /// <inheritdoc />
    public FusionCacheEvictionPolicyConfig Config { get; }

    /// <inheritdoc />
    public void OnGet(string key)
    {
        lock (_lock)
        {
            if (_map.TryGetValue(key, out var node))
            {
                _list.Remove(node);
                _list.AddLast(node);
            }
        }
    }

    /// <inheritdoc />
    public void OnSet(string key, FusionCacheEntryMetadata? metadata)
    {
        lock (_lock)
        {
            if (_map.TryGetValue(key, out var node))
            {
                // Move existing to most-recently used position
                _list.Remove(node);
                _list.AddLast(node);
            }
            else
            {
                // Add new
                var newNode = new LinkedListNode<string>(key);
                _list.AddLast(newNode);
                _map[key] = newNode;
            }
        }
    }

    /// <inheritdoc />
    public void OnRemove(string key)
    {
        lock (_lock)
        {
            if (_map.TryGetValue(key, out var node))
            {
                _list.Remove(node);
                _map.Remove(key);
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
            // check threshold
            var thresholdCount = (int)Math.Ceiling(capacity.Value * Config.EvictionThreshold);
            if (_map.Count <= thresholdCount)
                return result;

            var batchToRemove = (int)Math.Ceiling(capacity.Value * Config.EvictionPercentage);
            batchToRemove = Math.Max(batchToRemove, Config.MinEvictionBatchSize);
            batchToRemove = Math.Min(batchToRemove, Config.MaxEvictionBatchSize);
            batchToRemove = Math.Min(batchToRemove, _map.Count);

            for (var i = 0; i < batchToRemove; i++)
            {
                var node = _list.First;
                if (node is null)
                    break;
                _list.RemoveFirst();
                _map.Remove(node.Value);
                result.Add(node.Value);
            }
        }
        return result;
    }
}
