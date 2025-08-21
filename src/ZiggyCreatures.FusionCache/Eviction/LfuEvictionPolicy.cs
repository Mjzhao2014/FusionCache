using System;
using System.Collections.Generic;
using System.Linq;
using ZiggyCreatures.Caching.Fusion.Internals;

namespace ZiggyCreatures.Caching.Fusion;

/// <summary>
/// Least Frequently Used eviction policy: maintains frequency counters per key
/// and per-frequency lists to break ties by recency. When capacity is exceeded,
/// evicts entries with the lowest frequency, and within the same frequency
/// the least recently used entries.
/// </summary>
public class LfuEvictionPolicy : IFusionCacheEvictionPolicy
{
   private class KeyInfo
   {
       public int Frequency;
       public LinkedListNode<string> Node;
   }

   private readonly FusionCacheEvictionPolicyConfig _config;
   private readonly Dictionary<string, KeyInfo> _keyMap = new();
   private readonly Dictionary<int, LinkedList<string>> _freqLists = new();
   private int _minFrequency = 0;
   private readonly object _lock = new();

   public LfuEvictionPolicy(FusionCacheEvictionPolicyConfig config)
   {
       _config = config ?? throw new ArgumentNullException(nameof(config));
   }

   public string Name => "LFU";

   public FusionCacheEvictionPolicyConfig Config => _config;

   public void OnGet(string key)
   {
       lock (_lock)
       {
           if (_keyMap.TryGetValue(key, out var info))
           {
               UpdateFrequencyLocked(key, info);
           }
       }
   }

   public void OnSet(string key, FusionCacheEntryMetadata? metadata)
   {
       lock (_lock)
       {
           if (_keyMap.TryGetValue(key, out var info))
           {
               UpdateFrequencyLocked(key, info);
           }
           else
           {
               var list = GetFreqList(1);
               var node = list.AddLast(key);
               _keyMap[key] = new KeyInfo { Frequency = 1, Node = node };
               _minFrequency = 1;
           }
       }
   }

   public void OnRemove(string key)
   {
       lock (_lock)
       {
           if (_keyMap.TryGetValue(key, out var info))
           {
               var freq = info.Frequency;
               var list = _freqLists[freq];
               list.Remove(info.Node);
               if (list.Count == 0)
               {
                   _freqLists.Remove(freq);
                   if (_minFrequency == freq)
                   {
                       _minFrequency = _freqLists.Count > 0 ? _freqLists.Keys.Min() : 0;
                   }
               }
               _keyMap.Remove(key);
           }
       }
   }

   private void UpdateFrequencyLocked(string key, KeyInfo info)
   {
       var currentFreq = info.Frequency;
       var list = _freqLists[currentFreq];
       list.Remove(info.Node);
       if (list.Count == 0)
       {
           _freqLists.Remove(currentFreq);
           if (_minFrequency == currentFreq)
           {
               _minFrequency++;
           }
       }
       var newFreq = currentFreq + 1;
       var newList = GetFreqList(newFreq);
       var newNode = newList.AddLast(key);
       info.Frequency = newFreq;
       info.Node = newNode;
       _keyMap[key] = info;
   }

   private LinkedList<string> GetFreqList(int freq)
   {
       if (!_freqLists.TryGetValue(freq, out var list))
       {
           list = new LinkedList<string>();
           _freqLists[freq] = list;
       }
       return list;
   }

   /// <summary>
   /// Returns up to the configured number of keys with the lowest frequency
   /// (and in case of ties the least recently used) to evict.
   /// </summary>
   public IEnumerable<string> GetKeysToEvict()
   {
       lock (_lock)
       {
           if (!_config.MaxEntryCount.HasValue)
               return Array.Empty<string>();
           int currentCount = _keyMap.Count;
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
           int freq = _minFrequency;
           while (keys.Count < batchSize && _freqLists.Count > 0)
           {
               if (_freqLists.TryGetValue(freq, out var list))
               {
                   var node = list.First;
                   while (node != null && keys.Count < batchSize)
                   {
                       keys.Add(node.Value);
                       node = node.Next;
                   }
               }
               freq++;
           }
           return keys;
       }
   }
}
