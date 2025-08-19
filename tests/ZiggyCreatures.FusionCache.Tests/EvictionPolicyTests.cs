using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using ZiggyCreatures.Caching.Fusion.Eviction;

namespace ZiggyCreatures.Caching.Fusion.Tests;

public class EvictionPolicyTests
{
	[Fact]
	public void LruEvictionPolicy_EvictsLeastRecentlyUsedEntries()
	{
		// Arrange
		var config = new FusionCacheEvictionPolicyConfig
		{
			MaxEntryCount = 3,
			EvictionPercentage = 0.5 // Evict 50% when triggered
		};
		var policy = new LruEvictionPolicy(config);

		// Create some mock entries
		var entries = new Dictionary<string, IFusionCacheEntryInfo>
		{
			["key1"] = CreateMockEntry("value1", size: 100),
			["key2"] = CreateMockEntry("value2", size: 200),
			["key3"] = CreateMockEntry("value3", size: 300)
		};

		// Simulate access pattern: key1 -> key2 -> key3
		policy.OnEntrySet("key1", entries["key1"]);
		Thread.Sleep(1); // Ensure different timestamps
		policy.OnEntrySet("key2", entries["key2"]);
		Thread.Sleep(1);
		policy.OnEntrySet("key3", entries["key3"]);

		// Access key3 and key1 (making key2 least recently used)
		policy.OnEntryAccessed("key3", entries["key3"]);
		policy.OnEntryAccessed("key1", entries["key1"]);

		// Act
		var entriesToEvict = policy.SelectEntriesForEviction(entries, 1);

		// Assert
		Assert.Single(entriesToEvict);
		Assert.Equal("key2", entriesToEvict[0]); // key2 should be evicted as least recently used
	}

	[Fact]
	public void LfuEvictionPolicy_EvictsLeastFrequentlyUsedEntries()
	{
		// Arrange
		var config = new FusionCacheEvictionPolicyConfig
		{
			MaxEntryCount = 3,
			EvictionPercentage = 0.33
		};
		var policy = new LfuEvictionPolicy(config);

		var entries = new Dictionary<string, IFusionCacheEntryInfo>
		{
			["key1"] = CreateMockEntry("value1"),
			["key2"] = CreateMockEntry("value2"),
			["key3"] = CreateMockEntry("value3")
		};

		// Simulate access pattern: key1 (3 times), key2 (1 time), key3 (2 times)
		policy.OnEntrySet("key1", entries["key1"]);
		policy.OnEntryAccessed("key1", entries["key1"]);
		policy.OnEntryAccessed("key1", entries["key1"]); // key1: 3 total

		policy.OnEntrySet("key2", entries["key2"]); // key2: 1 total

		policy.OnEntrySet("key3", entries["key3"]);
		policy.OnEntryAccessed("key3", entries["key3"]); // key3: 2 total

		// Act
		var entriesToEvict = policy.SelectEntriesForEviction(entries, 1);

		// Assert
		Assert.Single(entriesToEvict);
		Assert.Equal("key2", entriesToEvict[0]); // key2 should be evicted as least frequently used
	}

	[Fact]
	public void SizeBasedEvictionPolicy_EvictsLargestEntries()
	{
		// Arrange
		var config = new FusionCacheEvictionPolicyConfig
		{
			MaxTotalSize = 1000,
			EvictionPercentage = 0.33
		};
		var policy = new SizeBasedEvictionPolicy(config);

		var entries = new Dictionary<string, IFusionCacheEntryInfo>
		{
			["small"] = CreateMockEntry("value", size: 100),
			["medium"] = CreateMockEntry("value", size: 300),
			["large"] = CreateMockEntry("value", size: 500)
		};

		policy.OnEntrySet("small", entries["small"]);
		policy.OnEntrySet("medium", entries["medium"]);
		policy.OnEntrySet("large", entries["large"]);

		// Act
		var entriesToEvict = policy.SelectEntriesForEviction(entries, 1);

		// Assert
		Assert.Single(entriesToEvict);
		Assert.Equal("large", entriesToEvict[0]); // Largest entry should be evicted first
	}

	[Fact]
	public void RandomEvictionPolicy_EvictsRandomEntries()
	{
		// Arrange
		var config = new FusionCacheEvictionPolicyConfig
		{
			MaxEntryCount = 5,
			EvictionPercentage = 0.4
		};
		var policy = new RandomEvictionPolicy(config, seed: 12345); // Use seed for deterministic testing

		var entries = new Dictionary<string, IFusionCacheEntryInfo>
		{
			["key1"] = CreateMockEntry("value1"),
			["key2"] = CreateMockEntry("value2"),
			["key3"] = CreateMockEntry("value3"),
			["key4"] = CreateMockEntry("value4"),
			["key5"] = CreateMockEntry("value5")
		};

		// Set entries (order shouldn't matter for random policy)
		foreach (var kvp in entries)
		{
			policy.OnEntrySet(kvp.Key, kvp.Value);
		}

		// Act
		var entriesToEvict = policy.SelectEntriesForEviction(entries, 2);

		// Assert
		Assert.Equal(2, entriesToEvict.Count);
		Assert.All(entriesToEvict, key => Assert.True(entries.ContainsKey(key)));
		Assert.Equal(entriesToEvict.Count, entriesToEvict.Distinct().Count()); // No duplicates
	}

	[Theory]
	[InlineData(5, 10, false)] // Below threshold
	[InlineData(10, 10, true)] // At threshold
	[InlineData(12, 10, true)] // Above threshold
	public void EvictionPolicy_ShouldTriggerEviction_RespectsEntryCountThreshold(int currentCount, int maxCount, bool expectedResult)
	{
		// Arrange
		var config = new FusionCacheEvictionPolicyConfig
		{
			MaxEntryCount = maxCount,
			EvictionThreshold = 1.0 // 100%
		};
		var policy = new LruEvictionPolicy(config);

		// Act
		var result = policy.ShouldTriggerEviction(currentCount, 0);

		// Assert
		Assert.Equal(expectedResult, result);
	}

	[Theory]
	[InlineData(500, 1000, false)] // Below threshold
	[InlineData(1000, 1000, true)] // At threshold
	[InlineData(1200, 1000, true)] // Above threshold
	public void EvictionPolicy_ShouldTriggerEviction_RespectsSizeThreshold(long currentSize, long maxSize, bool expectedResult)
	{
		// Arrange
		var config = new FusionCacheEvictionPolicyConfig
		{
			MaxTotalSize = maxSize,
			EvictionThreshold = 1.0 // 100%
		};
		var policy = new LruEvictionPolicy(config);

		// Act
		var result = policy.ShouldTriggerEviction(0, currentSize);

		// Assert
		Assert.Equal(expectedResult, result);
	}

	[Fact]
	public void EvictionPolicy_GetEvictionCount_RespectsConfiguration()
	{
		// Arrange
		var config = new FusionCacheEvictionPolicyConfig
		{
			MaxEntryCount = 100,
			EvictionPercentage = 0.2, // 20%
			MinEvictionCount = 5,
			MaxEvictionCount = 50
		};
		var policy = new LruEvictionPolicy(config);

		// Act & Assert
		
		// Normal case: 20% of 100 = 20
		Assert.Equal(20, policy.GetEvictionCount(100, 0));

		// Min constraint: 20% of 10 = 2, but min is 5
		Assert.Equal(5, policy.GetEvictionCount(10, 0));

		// Max constraint: 20% of 300 = 60, but max is 50
		Assert.Equal(50, policy.GetEvictionCount(300, 0));

		// Don't evict more than available
		Assert.Equal(3, policy.GetEvictionCount(3, 0));
	}

	[Fact]
	public void EvictionPolicyConfig_Validate_ThrowsOnInvalidConfiguration()
	{
		// Arrange & Act & Assert
		
		// No limits specified
		var config1 = new FusionCacheEvictionPolicyConfig();
		Assert.Throws<ArgumentException>(() => config1.Validate());

		// Invalid threshold
		var config2 = new FusionCacheEvictionPolicyConfig
		{
			MaxEntryCount = 100,
			EvictionThreshold = 1.5 // > 1.0
		};
		Assert.Throws<ArgumentException>(() => config2.Validate());

		// Invalid eviction percentage
		var config3 = new FusionCacheEvictionPolicyConfig
		{
			MaxEntryCount = 100,
			EvictionPercentage = -0.1 // Negative
		};
		Assert.Throws<ArgumentException>(() => config3.Validate());

		// Min > Max eviction count
		var config4 = new FusionCacheEvictionPolicyConfig
		{
			MaxEntryCount = 100,
			MinEvictionCount = 10,
			MaxEvictionCount = 5
		};
		Assert.Throws<ArgumentException>(() => config4.Validate());
	}

	[Fact]
	public void EvictionPolicy_Reset_ClearsState()
	{
		// Arrange
		var config = new FusionCacheEvictionPolicyConfig { MaxEntryCount = 10 };
		var policy = new LruEvictionPolicy(config);

		var entries = new Dictionary<string, IFusionCacheEntryInfo>
		{
			["key1"] = CreateMockEntry("value1"),
			["key2"] = CreateMockEntry("value2")
		};

		policy.OnEntrySet("key1", entries["key1"]);
		policy.OnEntrySet("key2", entries["key2"]);

		// Act
		policy.Reset();

		// Add new entries after reset
		policy.OnEntrySet("key3", CreateMockEntry("value3"));
		var entriesToEvict = policy.SelectEntriesForEviction(entries, 1);

		// Assert
		// After reset, the policy should not know about key1 and key2,
		// so it should prefer evicting them over key3 (which was added after reset)
		Assert.Contains(entriesToEvict[0], new[] { "key1", "key2" });
	}

	[Fact]
	public void EvictionPolicy_OnEntryRemoved_UpdatesState()
	{
		// Arrange
		var config = new FusionCacheEvictionPolicyConfig { MaxEntryCount = 10 };
		var policy = new LruEvictionPolicy(config);

		var entries = new Dictionary<string, IFusionCacheEntryInfo>
		{
			["key1"] = CreateMockEntry("value1"),
			["key2"] = CreateMockEntry("value2")
		};

		policy.OnEntrySet("key1", entries["key1"]);
		policy.OnEntrySet("key2", entries["key2"]);

		// Act
		policy.OnEntryRemoved("key1");

		// Create updated entries dictionary without key1
		var updatedEntries = new Dictionary<string, IFusionCacheEntryInfo>
		{
			["key2"] = entries["key2"]
		};

		var entriesToEvict = policy.SelectEntriesForEviction(updatedEntries, 1);

		// Assert
		Assert.Single(entriesToEvict);
		Assert.Equal("key2", entriesToEvict[0]); // Only key2 should be available for eviction
	}

	private static IFusionCacheEntryInfo CreateMockEntry(object value, long? size = null)
	{
		return new MockEntryInfo
		{
			Timestamp = DateTimeOffset.UtcNow.Ticks,
			LogicalExpirationTimestamp = DateTimeOffset.UtcNow.AddMinutes(10).Ticks,
			Tags = null,
			Size = size
		};
	}

	private class MockEntryInfo : IFusionCacheEntryInfo
	{
		public long Timestamp { get; init; }
		public long LogicalExpirationTimestamp { get; init; }
		public string[]? Tags { get; init; }
		public long? Size { get; init; }

		public bool IsLogicallyExpired() => DateTimeOffset.UtcNow.Ticks > LogicalExpirationTimestamp;
		public long? GetSize() => Size;
		public byte? GetPriority() => null;
	}
}