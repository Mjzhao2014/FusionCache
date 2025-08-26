using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;
using Xunit.Abstractions;
using ZiggyCreatures.Caching.Fusion;
using ZiggyCreatures.Caching.Fusion.Backplane;
using ZiggyCreatures.Caching.Fusion.Plugins;
using ZiggyCreatures.Caching.Fusion.Serialization.SystemTextJson;
using FusionCacheTests.Stuff;

namespace FusionCacheTests.RefactoringTests;

/// <summary>
/// Integration tests to verify that the refactored FusionCache architecture works correctly
/// and maintains backward compatibility with existing functionality.
/// </summary>
public class RefactoringIntegrationTests : AbstractTests
{
	public RefactoringIntegrationTests(ITestOutputHelper output)
		: base(output, null)
	{
	}

	private class SimpleTestPlugin : IFusionCachePlugin
	{
		public bool IsStarted { get; private set; }
		public bool IsStopped { get; private set; }

		public void Start(IFusionCache cache)
		{
			IsStarted = true;
		}

		public void Stop(IFusionCache cache)
		{
			IsStopped = true;
		}
	}

	private class MockDistributedCache : IDistributedCache
	{
		private readonly Dictionary<string, byte[]> _storage = new();

		public byte[]? Get(string key) => _storage.TryGetValue(key, out var value) ? value : null;
		public Task<byte[]?> GetAsync(string key, CancellationToken token = default) => Task.FromResult(Get(key));
		public void Refresh(string key) { }
		public Task RefreshAsync(string key, CancellationToken token = default) => Task.CompletedTask;
		public void Remove(string key) => _storage.Remove(key);
		public Task RemoveAsync(string key, CancellationToken token = default) { Remove(key); return Task.CompletedTask; }
		public void Set(string key, byte[] value, DistributedCacheEntryOptions options) => _storage[key] = value;
		public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default) { Set(key, value, options); return Task.CompletedTask; }
	}

	private class MockBackplane : IFusionCacheBackplane
	{
		public BackplaneSubscriptionOptions? SubscriptionOptions { get; private set; }
		public bool IsSubscribed { get; private set; }

		public void Subscribe(BackplaneSubscriptionOptions options)
		{
			SubscriptionOptions = options;
			IsSubscribed = true;
		}
		
		public ValueTask SubscribeAsync(BackplaneSubscriptionOptions options)
		{
			Subscribe(options);
			return default;
		}
		
		public void Unsubscribe()
		{
			IsSubscribed = false;
		}
		
		public ValueTask UnsubscribeAsync()
		{
			Unsubscribe();
			return default;
		}

		public void Publish(BackplaneMessage message, FusionCacheEntryOptions options, CancellationToken token = default) { }
		public ValueTask PublishAsync(BackplaneMessage message, FusionCacheEntryOptions options, CancellationToken token = default) => default;
	}

	[Fact]
	public void FusionCache_BasicOperations_WorkWithRefactoredArchitecture()
	{
		// Arrange
		var options = new FusionCacheOptions
		{
			CacheName = "RefactoredTest",
			DefaultEntryOptions = new FusionCacheEntryOptions
			{
				Duration = TimeSpan.FromMinutes(5)
			}
		};
		var cache = new FusionCache(Options.Create(options));

		// Act & Assert - Basic operations should work
		cache.Set("key1", "value1");
		var result = cache.GetOrDefault<string>("key1");
		Assert.Equal("value1", result);

		// Factory-based operations
		var factoryResult = cache.GetOrSet("key2", _ => "value2");
		Assert.Equal("value2", factoryResult);

		// Verify cache name and instance ID are accessible
		Assert.Equal("RefactoredTest", cache.CacheName);
		Assert.NotNull(cache.InstanceId);
		Assert.NotEmpty(cache.InstanceId);
	}

	[Fact]
	public void FusionCache_ConfigurationManager_HandlesOptionsCorrectly()
	{
		// Arrange
		var options = new FusionCacheOptions
		{
			CacheKeyPrefix = "test:",
			DisableTagging = false
		};
		var cache = new FusionCache(Options.Create(options));

		// Act & Assert - Configuration should be properly applied
		cache.Set("mykey", "myvalue");
		
		// CreateEntryOptions should work
		var entryOptions = cache.CreateEntryOptions(opt => opt.Duration = TimeSpan.FromHours(1));
		Assert.Equal(TimeSpan.FromHours(1), entryOptions.Duration);

		// Default entry options should be accessible
		Assert.NotNull(cache.DefaultEntryOptions);
	}

	[Fact]
	public void FusionCache_ComponentCoordinator_ManagesSerializerAndDistributedCache()
	{
		// Arrange
		var cache = new FusionCache(Options.Create(new FusionCacheOptions()));
		var serializer = new FusionCacheSystemTextJsonSerializer();
		var distributedCache = new MockDistributedCache();

		// Act
		cache.SetupSerializer(serializer);
		cache.SetupDistributedCache(distributedCache);

		// Assert
		Assert.True(cache.HasDistributedCache);
		Assert.Same(distributedCache, cache.DistributedCache);

		// Test distributed cache functionality
		cache.Set("dist-key", "dist-value");
		var result = cache.GetOrDefault<string>("dist-key");
		Assert.Equal("dist-value", result);

		// Remove distributed cache
		cache.RemoveDistributedCache();
		Assert.False(cache.HasDistributedCache);
		Assert.Null(cache.DistributedCache);
	}

	[Fact]
	public void FusionCache_ComponentCoordinator_ManagesBackplane()
	{
		// Arrange
		var options = new FusionCacheOptions { WaitForInitialBackplaneSubscribe = true };
		var cache = new FusionCache(Options.Create(options));
		var backplane = new MockBackplane();

		// Act
		cache.SetupBackplane(backplane);

		// Assert
		Assert.True(cache.HasBackplane);
		Assert.Same(backplane, cache.Backplane);
		Assert.True(backplane.IsSubscribed); // Should be subscribed

		// Remove backplane
		cache.RemoveBackplane();
		Assert.False(cache.HasBackplane);
		Assert.Null(cache.Backplane);
		Assert.False(backplane.IsSubscribed); // Should be unsubscribed
	}

	[Fact]
	public void FusionCache_PluginManager_HandlesPluginLifecycle()
	{
		// Arrange
		var cache = new FusionCache(Options.Create(new FusionCacheOptions()));
		var plugin = new SimpleTestPlugin();

		// Act - Add plugin
		cache.AddPlugin(plugin);

		// Assert
		Assert.True(plugin.IsStarted);
		Assert.False(plugin.IsStopped);

		// Act - Remove plugin
		var removed = cache.RemovePlugin(plugin);

		// Assert
		Assert.True(removed);
		Assert.True(plugin.IsStopped);
	}

	[Fact]
	public void FusionCache_FullConfiguration_WorksEndToEnd()
	{
		// Arrange
		var options = new FusionCacheOptions
		{
			CacheName = "FullTest",
			CacheKeyPrefix = "full:",
			DefaultEntryOptions = new FusionCacheEntryOptions
			{
				Duration = TimeSpan.FromMinutes(10)
			},
			WaitForInitialBackplaneSubscribe = true
		};

		var cache = new FusionCache(Options.Create(options));
		var serializer = new FusionCacheSystemTextJsonSerializer();
		var distributedCache = new MockDistributedCache();
		var backplane = new MockBackplane();
		var plugin = new SimpleTestPlugin();

		// Act - Set up full configuration
		cache.SetupSerializer(serializer);
		cache.SetupDistributedCache(distributedCache);
		cache.SetupBackplane(backplane);
		cache.AddPlugin(plugin);

		// Test basic operations
		cache.Set("test-key", "test-value");
		var result = cache.GetOrDefault<string>("test-key");

		// Assert
		Assert.Equal("FullTest", cache.CacheName);
		Assert.True(cache.HasDistributedCache);
		Assert.True(cache.HasBackplane);
		Assert.True(plugin.IsStarted);
		Assert.Equal("test-value", result);

		// Test factory operations
		var factoryResult = cache.GetOrSet("factory-key", _ => "factory-value");
		Assert.Equal("factory-value", factoryResult);

		// Cleanup
		cache.Dispose();
		Assert.True(plugin.IsStopped);
	}

	[Fact]
	public async Task FusionCache_AsyncOperations_WorkWithRefactoredArchitecture()
	{
		// Arrange
		var cache = new FusionCache(Options.Create(new FusionCacheOptions()));
		var serializer = new FusionCacheSystemTextJsonSerializer();
		var distributedCache = new MockDistributedCache();

		cache.SetupSerializer(serializer);
		cache.SetupDistributedCache(distributedCache);

		// Act & Assert - Async operations should work
		await cache.SetAsync("async-key", "async-value");
		var result = await cache.GetOrDefaultAsync<string>("async-key");
		Assert.Equal("async-value", result);

		// Factory-based async operations
		var factoryResult = await cache.GetOrSetAsync("async-factory-key", async _ =>
		{
			await Task.Delay(1); // Simulate async work
			return "async-factory-value";
		});
		Assert.Equal("async-factory-value", factoryResult);
	}

	[Fact]
	public void FusionCache_Events_WorkWithRefactoredArchitecture()
	{
		// Arrange
		var cache = new FusionCache(Options.Create(new FusionCacheOptions()));
		var events = new System.Collections.Concurrent.ConcurrentBag<string>();

		cache.Events.Memory.Hit += (s, e) => events.Add("hit");
		cache.Events.Memory.Miss += (s, e) => events.Add("miss");
		cache.Events.Memory.Set += (s, e) => events.Add("set");

		// Act
		cache.Set("event-key", "event-value"); // Should trigger a Set event
		var result1 = cache.GetOrDefault<string>("event-key"); // Should be a hit
		var result2 = cache.GetOrDefault<string>("nonexistent-key"); // Should be a miss

		// Assert basic functionality
		Assert.Equal("event-value", result1);
		Assert.Null(result2);
		
		// Give time for any background operations to complete
		System.Threading.Thread.Sleep(50);
		
		// Count events
		var eventArray = events.ToArray();
		var setCount = eventArray.Count(x => x == "set");
		var hitCount = eventArray.Count(x => x == "hit");
		var missCount = eventArray.Count(x => x == "miss");
		
		// Basic verification - at least one of each expected event should occur
		Assert.True(setCount >= 1, $"Expected at least 1 set event, got {setCount}. Events: [{string.Join(", ", eventArray)}]");
		Assert.True(hitCount >= 1, $"Expected at least 1 hit event, got {hitCount}. Events: [{string.Join(", ", eventArray)}]");  
		Assert.True(missCount >= 1, $"Expected at least 1 miss event, got {missCount}. Events: [{string.Join(", ", eventArray)}]");
	}

	[Fact]
	public void FusionCache_BackwardCompatibility_MaintainsExistingBehavior()
	{
		// Arrange - Use original constructor pattern
		var options = new FusionCacheOptions();
		var memoryCache = new MemoryCache(new MemoryCacheOptions());
		var logger = CreateXUnitLogger<FusionCache>();

		// Act - This should work exactly as before
		var cache = new FusionCache(Options.Create(options), memoryCache, logger);

		// Assert - All original functionality should work
		Assert.NotNull(cache.CacheName);
		Assert.NotNull(cache.InstanceId);
		Assert.NotNull(cache.DefaultEntryOptions);
		Assert.NotNull(cache.Events);

		// Basic operations
		cache.Set("compat-key", "compat-value");
		var result = cache.GetOrDefault<string>("compat-key");
		Assert.Equal("compat-value", result);

		// Configuration operations
		var entryOptions = cache.CreateEntryOptions();
		Assert.NotNull(entryOptions);

		cache.Dispose();
	}

	[Fact]
	public void FusionCache_ConfigurationValidation_WorksCorrectly()
	{
		// Arrange
		var logger = CreateListLogger<FusionCache>(LogLevel.Warning);
		var memoryCache = new MemoryCache(new MemoryCacheOptions());
		
		// Test named cache without prefix warning
		var options = new FusionCacheOptions 
		{ 
			CacheName = "NamedCacheTest",
			// No CacheKeyPrefix specified - should trigger warning
		};

		// Act
		var cache = new FusionCache(Options.Create(options), memoryCache, logger);

		// Assert - Should log warning about missing cache key prefix
		var logs = logger.Items;
		Assert.Contains(logs, log => log.Message.Contains("CacheKeyPrefix") && log.Message.Contains("named cache"));

		cache.Dispose();
	}

	[Fact]
	public void FusionCache_TaggingOperations_WorkWithRefactoredArchitecture()
	{
		// Arrange
		var options = new FusionCacheOptions { DisableTagging = false };
		var cache = new FusionCache(Options.Create(options));

		// Act & Assert - Tagging operations should work
		var tags = new[] { "tag1", "tag2" };
		cache.Set("tagged-key", "tagged-value", tags: tags);
		
		var result = cache.GetOrDefault<string>("tagged-key");
		Assert.Equal("tagged-value", result);

		// Remove by tag should work
		cache.RemoveByTag("tag1");
		var afterRemoval = cache.GetOrDefault<string>("tagged-key");
		Assert.Null(afterRemoval);
	}
}