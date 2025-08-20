 Feature Overview

  Implement a comprehensive L1 (memory) cache eviction policy system for FusionCache that automatically removes
  cache entries when capacity limits are reached, while maintaining O(1) performance and backward compatibility.

  Core Requirements

  Required Eviction Policies

  - LRU (Least Recently Used) - Evicts entries not accessed for longest time

    - When capacity is exceeded, evict the entry that was least recently accessed.
"Recent" is determined by the last access timestamp (read or write).
    - No duplicate entries should exist for the same key.
    - On set/get, the existing item must be repositioned as most recently used.
    - O(1) for get/set/remove operations

  - LFU (Least Frequently Used) - Evicts entries with lowest access frequency
    - When capacity is exceeded, evict the entry with the lowest access frequency.
    - If multiple entries share the same frequency, evict the least recently used among them (tie-breaker).
    - Each access increments frequency count.
    - No duplicate entries for the same key.
    - On set/get, increase frequency and adjust placement.
    - O(1) for get/set/remove operations


  Eviction Timing & Integration
  - Trigger Point: Check eviction thresholds after SET operations in MemoryCacheAccessor.SetEntry()
  - Policy Interface: Must expose methods for OnGet(key), OnSet(key, metadata), OnRemove(key), and GetKeysToEvict()
  - Performance: Maintain O(1) performance for Get/Set operations
  - Fire eviction event  with policy name and EvictionReason = Capacity. Ensure that duplicate events are not triggered.

Configuration options:
- Capacity Limits: Support entry count limits
 - Thresholds: Configurable trigger points (e.g., evict at 80% capacity)
 - Eviction Amount: Configurable percentage/count of entries to remove
 - Constraints: Min/max limits on eviction batch sizes


 Configuration Requirements

```
  public class FusionCacheEvictionPolicyConfig
  {
      public int? MaxEntryCount { get; set; }           // Max entries before eviction
      public double EvictionThreshold { get; set; } = 1.0;  // Trigger at % capacity (0.0-1.0)
      public double EvictionPercentage { get; set; } = 0.1; // % of max entries to evict when triggered
      public int MinEvictionBatchSize { get; set; } = 1;        // Minimum entries to evict
      public int MaxEvictionBatchSize{ get; set; } = 1000;     // Maximum entries to evict
  }
 ```



  API Design 

```
  // IFusionCacheEvictionPolicy interface 
  public interface IFusionCacheEvictionPolicy
  {
      string Name { get; }
	  FusionCacheEvictionPolicyConfig Config { get; }	
      void OnGet(string key);
      void OnSet(string key, FusionCacheEntryMetadata? metadata);
      void OnRemove(string key);
      IEnumerable<string> GetKeysToEvict();
  }
  
public class FusionCacheOptions
  {
      // Add this property 
      public IFusionCacheEvictionPolicy? EvictionPolicy { get; set; }
  }

public class LruEvictionPolicy : IFusionCacheEvictionPolicy
public class LfuEvictionPolicy : IFusionCacheEvictionPolicy

//Update  to FusionCacheMemoryEventsHub:
	void OnEviction(string operationId, string key, EvictionReason reason, string? policyName, object? value)

//Update FusionCacheEntryEvictionEventArgs
public FusionCacheEntryEvictionEventArgs(string key, EvictionReason reason,  string policyName, object? value)

//Fluent API Extensions

  public static class FusionCacheEvictionExtensions
  {
      // LRU extensions
      public static IFusionCacheBuilder WithLruEviction(this IFusionCacheBuilder builder, int maxEntryCount, double
   evictionPercentage = 0.1);
      public static IFusionCacheBuilder WithLruEviction(this IFusionCacheBuilder builder,
  FusionCacheEvictionPolicyConfig config);

      // LFU extensions
      public static IFusionCacheBuilder WithLfuEviction(this IFusionCacheBuilder builder, int maxEntryCount, double
   evictionPercentage = 0.1);
      public static IFusionCacheBuilder WithLfuEviction(this IFusionCacheBuilder builder,
  FusionCacheEvictionPolicyConfig config);

  }

  //Usage Examples

  // LRU eviction with entry count limit
  var options = new FusionCacheOptions
  {
      EvictionPolicy = new LruEvictionPolicy(new FusionCacheEvictionPolicyConfig
      {
          MaxEntryCount = 1000,
          EvictionPercentage = 0.1
      })
  };


```

Make sure the existing behavior is unchanged, if the cache does not apply eviction policy. 
Don't need to generate test cases. 
Make sure your solution compiles via "dotnet build" command

