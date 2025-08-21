using System;
using System.Collections.Generic;
using ZiggyCreatures.Caching.Fusion.Internals;

namespace ZiggyCreatures.Caching.Fusion;

/// <summary>
/// Least Recently Used eviction policy: maintains an ordered list of keys
/// based on last access time. When capacity is exceeded, evicts the
/// least recently used entries.
/// </summary>
public class LruEvictionPolicy : IFusionCacheEvictionPolicy
{
   private readonly FusionCacheEvictionPolicyConfig _config;
   // head = LRU, tail = MRU
   private readonly LinkedList<string> _lruList = new();
   private readonly Dictionary<string, LinkedListNode<string>> _map = new();
   private readonly object _lock = new();

   public LruEvictionPolicy(FusionCacheEvictionPolicyConfig config)
   {
       _config = config ?? throw new ArgumentNullException(nameof(config));
   }

   public string Name => "LRU";

   public FusionCacheEvictionPolicyConfig Config => _config;

   public void OnGet(string key)
   {
       lock (_lock)
       {
           if (_map.TryGetValue(key, out var node))
           {
               _lruList.Remove(node);
               _lruList.AddLast(node);
           }
       }
   }

   public void OnSet(string key, FusionCacheEntryMetadata? metadata)
   {
       lock (_lock)
       {
           if (_map.TryGetValue(key, out var node))
           {
               _lruList.Remove(node);
               _lruList.AddLast(node);
           }
           else
           {
               var newNode = _lruList.AddLast(key);
               _map[key] = newNode;
           }
       }
   }

   public void OnRemove(string key)
   {
       lock (_lock)
       {
           if (_map.TryGetValue(key, out var node))
           {
               _lruList.Remove(node);
               _map.Remove(key);
           }
       }
   }

   /// <summary>
   /// Return up to the configured number of least recently used keys to evict.
   /// </summary>
   public IEnumerable<string> GetKeysToEvict()
   {
       lock (_lock)
       {
           if (!_config.MaxEntryCount.HasValue)
               return Array.Empty<string>();

           int currentCount = _map.Count;
           int capacity = _config.MaxEntryCount.Value;
           int threshold = (int)Math.Floor(capacity * _config.EvictionThreshold);
           if (currentCount <= threshold)
               return Array.Empty<string>();

           int batchSize = (int)Math.Ceiling(capacity * _config.EvictionPercentage);
           if (batchSize < _config.MinEvictionBatchSize)
               batchSize = _config.MinEvictionBatchSize;
           if (batchSize > _config.MaxEvictionBatchSize)
               batchSize = _config.MaxEvictionBatchSize;
           var keys = new List<string>(batchSize);
           var node = _lruList.First;
           while (node != null && keys.Count < batchSize)
           {
               keys.Add(node.Value);
               node = node.Next;
           }
           return keys;
       }
   }
}
