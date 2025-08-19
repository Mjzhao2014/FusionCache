using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Xunit;
using ZiggyCreatures.Caching.Fusion.Events;

namespace ZiggyCreatures.Caching.Fusion.Tests;

public class EvictionEventTests
{
	[Fact]
	public async Task FusionCache_EvictionPolicy_FiresEvictionEvents()
	{
		// Arrange
		var evictionEvents = new List<FusionCacheEntryEvictionEventArgs>();
		var options = new FusionCacheOptions
		{
			DefaultEntryOptions = new FusionCacheEntryOptions { Duration = TimeSpan.FromMinutes(10) },
			EvictionPolicy = new LruEvictionPolicy(new FusionCacheEvictionPolicyConfig
			{
				MaxEntryCount = 3,
				EvictionPercentage = 0.5
			})
		};

		var cache = new FusionCache(options);

		// Subscribe to eviction events
		cache.Events.Memory.Eviction += (sender, args) =>
		{
			evictionEvents.Add(args);
		};

		using (cache)
		{
			// Act - Fill cache beyond capacity
			await cache.SetAsync("key1", "value1");
			await cache.SetAsync("key2", "value2");
			await cache.SetAsync("key3", "value3");
			
			// This should trigger eviction
			await cache.SetAsync("key4", "value4");

			// Allow some time for async eviction processing
			await Task.Delay(50);

			// Assert
			Assert.NotEmpty(evictionEvents);
			Assert.True(evictionEvents.Count >= 1, $"Expected at least 1 eviction event, got {evictionEvents.Count}");
			
			var evictionEvent = evictionEvents[0];
			Assert.NotNull(evictionEvent.Key);
			Assert.Contains(evictionEvent.Key, new[] { "key1", "key2", "key3" }); // One of the first 3 should be evicted
		}
	}

	[Fact]
	public async Task FusionCache_LruEvictionPolicy_FiresEventsWithCorrectValues()
	{
		// Arrange
		var evictionEvents = new List<FusionCacheEntryEvictionEventArgs>();
		var options = new FusionCacheOptions
		{
			DefaultEntryOptions = new FusionCacheEntryOptions { Duration = TimeSpan.FromMinutes(10) },
			EvictionPolicy = new LruEvictionPolicy(new FusionCacheEvictionPolicyConfig
			{
				MaxEntryCount = 2,
				EvictionPercentage = 0.5
			})
		};

		var cache = new FusionCache(options);

		// Subscribe to eviction events
		cache.Events.Memory.Eviction += (sender, args) =>
		{
			evictionEvents.Add(args);
		};

		using (cache)
		{
			// Act
			await cache.SetAsync("first", "value1");
			await cache.SetAsync("second", "value2");
			
			// Access first to make it more recently used
			await cache.GetOrDefaultAsync<string>("first");
			
			// Add third entry - should evict "second" (least recently used)
			await cache.SetAsync("third", "value3");

			await Task.Delay(50);

			// Assert
			Assert.Single(evictionEvents);
			var evictionEvent = evictionEvents[0];
			Assert.Equal("second", evictionEvent.Key);
			Assert.Equal("value2", evictionEvent.Value);
		}
	}

	[Fact]
	public async Task FusionCache_SizeBasedEvictionPolicy_FiresEventsWithCorrectValues()
	{
		// Arrange
		var evictionEvents = new List<FusionCacheEntryEvictionEventArgs>();
		var options = new FusionCacheOptions
		{
			DefaultEntryOptions = new FusionCacheEntryOptions { Duration = TimeSpan.FromMinutes(10) },
			EvictionPolicy = new SizeBasedEvictionPolicy(new FusionCacheEvictionPolicyConfig
			{
				MaxTotalSize = 1000,
				EvictionPercentage = 0.5
			})
		};

		var cache = new FusionCache(options);

		// Subscribe to eviction events
		cache.Events.Memory.Eviction += (sender, args) =>
		{
			evictionEvents.Add(args);
		};

		using (cache)
		{
			// Act - Add entries with different sizes
			await cache.SetAsync("small", "tiny", new FusionCacheEntryOptions { Size = 100 });
			await cache.SetAsync("medium", "medium-data", new FusionCacheEntryOptions { Size = 300 });
			await cache.SetAsync("large", "very-large-content", new FusionCacheEntryOptions { Size = 500 });
			
			// This should trigger eviction - largest entry should be evicted first
			await cache.SetAsync("trigger", "trigger-content", new FusionCacheEntryOptions { Size = 200 });

			await Task.Delay(50);

			// Assert
			Assert.NotEmpty(evictionEvents);
			var evictionEvent = evictionEvents.First();
			Assert.Equal("large", evictionEvent.Key);
			Assert.Equal("very-large-content", evictionEvent.Value);
		}
	}

	[Fact]
	public async Task FusionCache_MultipleEvictions_FiresMultipleEvents()
	{
		// Arrange
		var evictionEvents = new List<FusionCacheEntryEvictionEventArgs>();
		var options = new FusionCacheOptions
		{
			DefaultEntryOptions = new FusionCacheEntryOptions { Duration = TimeSpan.FromMinutes(10) },
			EvictionPolicy = new LruEvictionPolicy(new FusionCacheEvictionPolicyConfig
			{
				MaxEntryCount = 5,
				EvictionPercentage = 0.6 // Evict 60% = 3 entries when triggered
			})
		};

		var cache = new FusionCache(options);

		// Subscribe to eviction events
		cache.Events.Memory.Eviction += (sender, args) =>
		{
			evictionEvents.Add(args);
		};

		using (cache)
		{
			// Act - Fill cache to capacity
			for (int i = 1; i <= 5; i++)
			{
				await cache.SetAsync($"key{i}", $"value{i}");
			}

			// Trigger eviction by adding another entry
			await cache.SetAsync("trigger", "trigger-value");

			await Task.Delay(100);

			// Assert
			Assert.True(evictionEvents.Count >= 2, $"Expected at least 2 eviction events, got {evictionEvents.Count}");
			
			// Verify all evicted keys are different
			var evictedKeys = evictionEvents.Select(e => e.Key).ToList();
			Assert.Equal(evictedKeys.Count, evictedKeys.Distinct().Count());
			
			// Verify all events have values
			Assert.All(evictionEvents, e => Assert.NotNull(e.Value));
		}
	}

	[Fact]
	public async Task FusionCache_EvictionEvents_ContainCorrectEventReason()
	{
		// Arrange
		var evictionEvents = new List<FusionCacheEntryEvictionEventArgs>();
		var options = new FusionCacheOptions
		{
			DefaultEntryOptions = new FusionCacheEntryOptions { Duration = TimeSpan.FromMinutes(10) },
			EvictionPolicy = new LruEvictionPolicy(new FusionCacheEvictionPolicyConfig
			{
				MaxEntryCount = 2,
				EvictionPercentage = 0.5
			})
		};

		var cache = new FusionCache(options);

		// Subscribe to eviction events
		cache.Events.Memory.Eviction += (sender, args) =>
		{
			evictionEvents.Add(args);
		};

		using (cache)
		{
			// Act
			await cache.SetAsync("key1", "value1");
			await cache.SetAsync("key2", "value2");
			await cache.SetAsync("key3", "value3"); // Should trigger eviction

			await Task.Delay(50);

			// Assert
			Assert.Single(evictionEvents);
			var evictionEvent = evictionEvents[0];
			
			// Policy evictions should use EvictionReason.None with policy name in metrics
			// (based on the current implementation in OnPolicyEviction)
			Assert.Equal(EvictionReason.None, evictionEvent.Reason);
		}
	}

	[Fact]
	public async Task FusionCache_WithoutEvictionPolicy_DoesNotFirePolicyEvictionEvents()
	{
		// Arrange
		var evictionEvents = new List<FusionCacheEntryEvictionEventArgs>();
		var options = new FusionCacheOptions
		{
			DefaultEntryOptions = new FusionCacheEntryOptions { Duration = TimeSpan.FromMinutes(10) }
			// No eviction policy configured
		};

		var cache = new FusionCache(options);

		// Subscribe to eviction events
		cache.Events.Memory.Eviction += (sender, args) =>
		{
			evictionEvents.Add(args);
		};

		using (cache)
		{
			// Act - Add many entries
			for (int i = 0; i < 100; i++)
			{
				await cache.SetAsync($"key{i}", $"value{i}");
			}

			await Task.Delay(100);

			// Assert - No policy eviction events should be fired
			// (though underlying IMemoryCache might still evict and fire events)
			var policyEvictions = evictionEvents.Where(e => e.Reason == EvictionReason.None).ToList();
			Assert.Empty(policyEvictions);
		}
	}

	[Fact]
	public async Task FusionCache_EvictionEvents_HandlesConcurrentAccess()
	{
		// Arrange
		var evictionEvents = new List<FusionCacheEntryEvictionEventArgs>();
		var evictionEventsLock = new object();
		
		var options = new FusionCacheOptions
		{
			DefaultEntryOptions = new FusionCacheEntryOptions { Duration = TimeSpan.FromMinutes(10) },
			EvictionPolicy = new LruEvictionPolicy(new FusionCacheEvictionPolicyConfig
			{
				MaxEntryCount = 10,
				EvictionPercentage = 0.3
			})
		};

		var cache = new FusionCache(options);

		// Subscribe to eviction events with thread-safe collection
		cache.Events.Memory.Eviction += (sender, args) =>
		{
			lock (evictionEventsLock)
			{
				evictionEvents.Add(args);
			}
		};

		using (cache)
		{
			// Act - Rapidly add entries from multiple threads
			var tasks = new List<Task>();
			for (int i = 0; i < 50; i++)
			{
				int index = i;
				tasks.Add(Task.Run(async () =>
				{
					await cache.SetAsync($"key{index}", $"value{index}");
					await cache.GetOrDefaultAsync<string>($"key{index}");
				}));
			}

			await Task.WhenAll(tasks);
			await Task.Delay(200); // Allow eviction events to complete

			// Assert
			lock (evictionEventsLock)
			{
				Assert.NotEmpty(evictionEvents);
				
				// Verify no duplicate keys in eviction events
				var evictedKeys = evictionEvents.Select(e => e.Key).ToList();
				Assert.Equal(evictedKeys.Count, evictedKeys.Distinct().Count());
				
				// Verify all events have valid data
				Assert.All(evictionEvents, e =>
				{
					Assert.NotNull(e.Key);
					Assert.NotNull(e.Value);
				});
			}
		}
	}
}