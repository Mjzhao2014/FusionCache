using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
// using ZiggyCreatures.Caching.Fusion.Eviction;

namespace ZiggyCreatures.Caching.Fusion.Tests;

public class EvictionTests
{
	[Fact]
	public async Task FusionCache_WithLruEviction_EvictsEntriesWhenCapacityReached()
	{
		// Arrange
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

		using (cache)
		{
			// Act - Fill cache to capacity
			await cache.SetAsync("key1", "value1");
			await cache.SetAsync("key2", "value2");
			await cache.SetAsync("key3", "value3");

			// Verify all entries are present
			Assert.Equal("value1", await cache.GetOrDefaultAsync<string>("key1"));
			Assert.Equal("value2", await cache.GetOrDefaultAsync<string>("key2"));
			Assert.Equal("value3", await cache.GetOrDefaultAsync<string>("key3"));

			// Access key1 and key3 to make key2 least recently used
			await cache.GetOrDefaultAsync<string>("key1");
			await cache.GetOrDefaultAsync<string>("key3");

			// Add a 4th entry which should trigger eviction
			await cache.SetAsync("key4", "value4");

			// Assert - key2 should be evicted (least recently used)
			// Note: Due to timing and implementation details, we need to allow some tolerance
			await Task.Delay(50); // Allow eviction to complete

			var key1Value = await cache.GetOrDefaultAsync<string>("key1");
			var key2Value = await cache.GetOrDefaultAsync<string>("key2");
			var key3Value = await cache.GetOrDefaultAsync<string>("key3");
			var key4Value = await cache.GetOrDefaultAsync<string>("key4");

			// key2 should be evicted (least recently used)
			Assert.NotNull(key1Value);
			Assert.Null(key2Value); // This should be evicted
			Assert.NotNull(key3Value);
			Assert.NotNull(key4Value);
		}
	}

	[Fact]
	public async Task FusionCache_WithLfuEviction_EvictsLeastFrequentEntries()
	{
		// Arrange
		var options = new FusionCacheOptions
		{
			DefaultEntryOptions = new FusionCacheEntryOptions { Duration = TimeSpan.FromMinutes(10) },
			EvictionPolicy = new LfuEvictionPolicy(new FusionCacheEvictionPolicyConfig
			{
				MaxEntryCount = 3,
				EvictionPercentage = 0.34
			})
		};
		var cache = new FusionCache(options);

		using (cache)
		{
			// Act - Add entries with different access patterns
			await cache.SetAsync("frequent", "value1");
			await cache.SetAsync("medium", "value2");
			await cache.SetAsync("rare", "value3");

			// Create frequency differences
			for (int i = 0; i < 5; i++)
			{
				await cache.GetOrDefaultAsync<string>("frequent"); // 6 total accesses (1 set + 5 gets)
			}

			for (int i = 0; i < 2; i++)
			{
				await cache.GetOrDefaultAsync<string>("medium"); // 3 total accesses (1 set + 2 gets)
			}

			// "rare" only accessed during set (1 total access)

			// Add a 4th entry to trigger eviction
			await cache.SetAsync("new", "value4");

			await Task.Delay(50); // Allow eviction to complete

			// Assert - "rare" should be evicted (least frequently used)
			var frequentValue = await cache.GetOrDefaultAsync<string>("frequent");
			var mediumValue = await cache.GetOrDefaultAsync<string>("medium");
			var rareValue = await cache.GetOrDefaultAsync<string>("rare");
			var newValue = await cache.GetOrDefaultAsync<string>("new");

			Assert.NotNull(frequentValue);
			Assert.NotNull(mediumValue);
			Assert.Null(rareValue); // This should be evicted
			Assert.NotNull(newValue);
		}
	}

	[Fact]
	public async Task FusionCache_WithSizeBasedEviction_EvictsLargestEntries()
	{
		// Arrange
		var options = new FusionCacheOptions
		{
			DefaultEntryOptions = new FusionCacheEntryOptions 
			{ 
				Duration = TimeSpan.FromMinutes(10),
				Size = 100 // Default size
			},
			EvictionPolicy = new SizeBasedEvictionPolicy(new FusionCacheEvictionPolicyConfig
			{
				MaxTotalSize = 1000,
				EvictionPercentage = 0.5
			})
		};
		var cache = new FusionCache(options);

		using (cache)
		{
			// Act - Add entries with different sizes
			await cache.SetAsync("small", "tiny", new FusionCacheEntryOptions { Size = 100 });
			await cache.SetAsync("medium", "medium-sized", new FusionCacheEntryOptions { Size = 300 });
			await cache.SetAsync("large", "very-large-content", new FusionCacheEntryOptions { Size = 500 });

			// Verify all entries are present
			Assert.Equal("tiny", await cache.GetOrDefaultAsync<string>("small"));
			Assert.Equal("medium-sized", await cache.GetOrDefaultAsync<string>("medium"));
			Assert.Equal("very-large-content", await cache.GetOrDefaultAsync<string>("large"));

			// Add another entry that pushes us over the size limit
			await cache.SetAsync("trigger", "trigger-eviction", new FusionCacheEntryOptions { Size = 200 });

			await Task.Delay(50); // Allow eviction to complete

			// Assert - largest entry should be evicted first
			var smallValue = await cache.GetOrDefaultAsync<string>("small");
			var mediumValue = await cache.GetOrDefaultAsync<string>("medium");
			var largeValue = await cache.GetOrDefaultAsync<string>("large");
			var triggerValue = await cache.GetOrDefaultAsync<string>("trigger");

			Assert.NotNull(smallValue);
			Assert.NotNull(mediumValue);
			Assert.Null(largeValue); // This should be evicted (largest)
			Assert.NotNull(triggerValue);
		}
	}

	[Fact]
	public async Task FusionCache_WithEvictionPolicy_RespectsThresholds()
	{
		// Arrange
		var config = new FusionCacheEvictionPolicyConfig
		{
			MaxEntryCount = 10,
			EvictionThreshold = 0.8, // Trigger at 80% capacity (8 entries)
			EvictionPercentage = 0.3 // Evict 30% when triggered
		};

		var options = new FusionCacheOptions
		{
			DefaultEntryOptions = new FusionCacheEntryOptions { Duration = TimeSpan.FromMinutes(10) },
			EvictionPolicy = new LruEvictionPolicy(config)
		};
		var cache = new FusionCache(options);

		using (cache)
		{
			// Act - Add entries up to threshold
			for (int i = 0; i < 7; i++)
			{
				await cache.SetAsync($"key{i}", $"value{i}");
			}

			// Verify no eviction happened yet
			for (int i = 0; i < 7; i++)
			{
				var value = await cache.GetOrDefaultAsync<string>($"key{i}");
				Assert.NotNull(value);
			}

			// Add 8th entry - should trigger eviction at 80% of 10 = 8 entries
			await cache.SetAsync("key8", "value8");

			await Task.Delay(50); // Allow eviction to complete

			// Count remaining entries
			int remainingCount = 0;
			for (int i = 0; i < 8; i++)
			{
				var value = await cache.GetOrDefaultAsync<string>($"key{i}");
				if (value != null) remainingCount++;
			}

			// Should have evicted approximately 30% of 8 = ~2-3 entries
			// So we should have 5-6 entries remaining
			Assert.True(remainingCount >= 5 && remainingCount <= 6, 
				$"Expected 5-6 remaining entries, but found {remainingCount}");
		}
	}

	[Fact]
	public async Task FusionCache_WithoutEvictionPolicy_DoesNotEvict()
	{
		// Arrange
		var options = new FusionCacheOptions
		{
			DefaultEntryOptions = new FusionCacheEntryOptions { Duration = TimeSpan.FromMinutes(10) }
			// No eviction policy configured
		};
		var cache = new FusionCache(options);

		using (cache)
		{
			// Act - Add many entries
			for (int i = 0; i < 100; i++)
			{
				await cache.SetAsync($"key{i}", $"value{i}");
			}

			await Task.Delay(50);

			// Assert - All entries should still be present (no eviction)
			for (int i = 0; i < 100; i++)
			{
				var value = await cache.GetOrDefaultAsync<string>($"key{i}");
				Assert.NotNull(value);
			}
		}
	}

	[Fact]
	public async Task FusionCache_EvictionPolicy_WorksWithBackgroundOperations()
	{
		// Arrange
		var options = new FusionCacheOptions
		{
			DefaultEntryOptions = new FusionCacheEntryOptions 
			{ 
				Duration = TimeSpan.FromMinutes(10),
				AllowBackgroundDistributedCacheOperations = true
			},
			EvictionPolicy = new LruEvictionPolicy(new FusionCacheEvictionPolicyConfig
			{
				MaxEntryCount = 5,
				EvictionPercentage = 0.4
			})
		};
		var cache = new FusionCache(options);

		using (cache)
		{
			// Act - Rapidly add entries to test eviction under concurrent access
			var tasks = new List<Task>();
			for (int i = 0; i < 10; i++)
			{
				int index = i;
				tasks.Add(Task.Run(async () =>
				{
					await cache.SetAsync($"key{index}", $"value{index}");
					await cache.GetOrDefaultAsync<string>($"key{index}");
				}));
			}

			await Task.WhenAll(tasks);
			await Task.Delay(100); // Allow eviction to complete

			// Assert - Should have approximately 5 entries (capacity limit)
			int presentCount = 0;
			for (int i = 0; i < 10; i++)
			{
				var value = await cache.GetOrDefaultAsync<string>($"key{i}");
				if (value != null) presentCount++;
			}

			Assert.True(presentCount <= 5, $"Expected at most 5 entries, but found {presentCount}");
		}
	}

	// NOTE: This test is commented out because it requires integration with the FusionCacheBuilder
	// which would need to be done as part of the main FusionCache integration
	//[Fact]
	//public void EvictionExtensions_ConfigureCorrectly()
	//{
	//	// Test would verify that extension methods work correctly with the builder
	//}
}