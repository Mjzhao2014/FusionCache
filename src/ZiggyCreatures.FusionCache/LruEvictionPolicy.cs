using System;
using System.Collections.Generic;
using ZiggyCreatures.Caching.Fusion.Internals;

namespace ZiggyCreatures.Caching.Fusion;

/// <summary>
/// Least Recently Used eviction policy: maintains a linked list of keys ordered by last access (read or write).
/// When capacity is exceeded, keys that have not been used for the longest time are evicted first.
/// </summary>
public class LruEvictionPolicy : IFusionCacheEvictionPolicy
{
    private readonly LinkedList<string> _lruList = new LinkedList<string>();
    private readonly Dictionary<string, LinkedListNode<string>> _nodes = new Dictionary<string, LinkedListNode<string>>();
    private readonly object _lock = new object();

    /// <summary>
    /// Creates a new LRU eviction policy using the specified configuration.
    /// </summary>
    /// <param name="config">Eviction config.</param>
    public LruEvictionPolicy(FusionCacheEvictionPolicyConfig config)
    {
        Config = config;
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
                // Move to head
                _lruList.Remove(node);
                _lruList.AddFirst(node);
            }
        }
    }

    /// <inheritdoc />
    public void OnSet(string key, FusionCacheEntryMetadata? metadata)
    {
        lock (_lock)
        {
            if (_nodes.TryGetValue(key, out var node))
            {
                // Existing: move to head
                _lruList.Remove(node);
                _lruList.AddFirst(node);
            }
            else
            {
                // New entry
                var newNode = _lruList.AddFirst(key);
                _nodes[key] = newNode;
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
            if (_nodes.Count < thresholdCount)
                return result;

			var batchSize = (int)Math.Ceiling(max * Config.EvictionPercentage);
			// clamp to configured min/max batch size without relying on Math.Clamp (not available on all TFMs)
			if (batchSize < Config.MinEvictionBatchSize)
				batchSize = Config.MinEvictionBatchSize;
			if (batchSize > Config.MaxEvictionBatchSize)
				batchSize = Config.MaxEvictionBatchSize;

            var node = _lruList.Last;
            while (node != null && result.Count < batchSize)
            {
                result.Add(node.Value);
                node = node.Previous;
            }
            return result;
        }
    }
}
