using FusionCacheTests.Stuff;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Collections.Concurrent;
using Xunit;
using Xunit.Abstractions;
using ZiggyCreatures.Caching.Fusion;
using ZiggyCreatures.Caching.Fusion.Dependencies;
using ZiggyCreatures.Caching.Fusion.Backplane.Memory;

namespace FusionCacheTests;

public class DependencyTests : AbstractTests
{
	public DependencyTests(ITestOutputHelper output)
		: base(output, null)
	{
	}
	[Fact]
	public void BasicKeyDependency_InvalidatesChildWhenParentChanges()
	{
		using var cache = new FusionCache(new FusionCacheOptions());

		// Set parent value
		cache.Set("parent:1", "parent-value");

		// Set child with dependency on parent
		cache.Set("child:1", "child-value", options => options
			.SetDuration(TimeSpan.FromMinutes(10))
			.WithDependencies(DependsOn.Keys("parent:1")));

		// Verify both exist
		Assert.Equal("parent-value", cache.GetOrDefault<string>("parent:1"));
		Assert.Equal("child-value", cache.GetOrDefault<string>("child:1"));

		// Update parent (this should invalidate child)
		cache.Set("parent:1", "new-parent-value");

		// Parent should have new value, child should be gone
		Assert.Equal("new-parent-value", cache.GetOrDefault<string>("parent:1"));
		Assert.Null(cache.GetOrDefault<string>("child:1"));
	}

	[Fact]
	public void MultipleDependencies_InvalidatesOnAnyParentChange()
	{
		using var cache = new FusionCache(new FusionCacheOptions());

		// Set multiple parents
		cache.Set("parent:1", "value1");
		cache.Set("parent:2", "value2");

		// Set child depending on multiple parents
		cache.Set("child:complex", "complex-child", options => options
			.SetDuration(TimeSpan.FromMinutes(10))
			.WithDependencies(DependsOn.Keys("parent:1", "parent:2")));

		// Verify child exists
		Assert.Equal("complex-child", cache.GetOrDefault<string>("child:complex"));

		// Update first parent - should invalidate child
		cache.Set("parent:1", "new-value1");
		Assert.Null(cache.GetOrDefault<string>("child:complex"));

		// Re-add child
		cache.Set("child:complex", "complex-child", options => options
			.SetDuration(TimeSpan.FromMinutes(10))
			.WithDependencies(DependsOn.Keys("parent:1", "parent:2")));

		// Update second parent - should invalidate child again
		cache.Set("parent:2", "new-value2");
		Assert.Null(cache.GetOrDefault<string>("child:complex"));

		// Re-add child one more time
		cache.Set("child:complex", "complex-child", options => options
			.SetDuration(TimeSpan.FromMinutes(10))
			.WithDependencies(DependsOn.Keys("parent:1", "parent:2")));

	}

	[Fact]
	public void ChainedDependencies_CascadesThroughMultipleLevels()
	{
		using var cache = new FusionCache(new FusionCacheOptions());

		// Set up chain: grandparent -> parent -> child
		cache.Set("grandparent", "gp-value");

		cache.Set("parent", "parent-value", options => options
			.SetDuration(TimeSpan.FromMinutes(10))
			.WithDependencies(DependsOn.Keys("grandparent")));

		cache.Set("child", "child-value", options => options
			.SetDuration(TimeSpan.FromMinutes(10))
			.WithDependencies(DependsOn.Keys("parent")));

		// Verify all exist
		Assert.Equal("gp-value", cache.GetOrDefault<string>("grandparent"));
		Assert.Equal("parent-value", cache.GetOrDefault<string>("parent"));
		Assert.Equal("child-value", cache.GetOrDefault<string>("child"));

		// Update grandparent - should cascade to both parent and child
		cache.Set("grandparent", "new-gp-value");

		// Grandparent should have new value, others should be gone
		Assert.Equal("new-gp-value", cache.GetOrDefault<string>("grandparent"));
		Assert.Null(cache.GetOrDefault<string>("parent"));
		Assert.Null(cache.GetOrDefault<string>("child"));
	}

	[Fact]
	public async Task AsyncDependency_WorksCorrectly()
	{
		using var cache = new FusionCache(new FusionCacheOptions());

		// Set parent value
		await cache.SetAsync("async-parent", "parent-value");

		// Set child with dependency
		await cache.SetAsync("async-child", "child-value", options => options
			.SetDuration(TimeSpan.FromMinutes(10))
			.WithDependencies(DependsOn.Keys("async-parent")));

		// Verify both exist
		Assert.Equal("parent-value", await cache.GetOrDefaultAsync<string>("async-parent"));
		Assert.Equal("child-value", await cache.GetOrDefaultAsync<string>("async-child"));

		// Update parent async - should invalidate child
		await cache.SetAsync("async-parent", "new-parent-value");

		// Parent should have new value, child should be gone
		Assert.Equal("new-parent-value", await cache.GetOrDefaultAsync<string>("async-parent"));
		Assert.Null(await cache.GetOrDefaultAsync<string>("async-child"));
	}

	[Fact]
	public void GetOrSetWithDependencies_RegistersDependenciesCorrectly()
	{
		using var cache = new FusionCache(new FusionCacheOptions());

		// Set parent
		cache.Set("parent:config", "config-value");

		// Use GetOrSet with dependencies
		var result = cache.GetOrSet("derived:config", 
			_ => "derived-config-value",
			options => options
				.SetDuration(TimeSpan.FromMinutes(5))
				.WithDependencies(DependsOn.Keys("parent:config")));

		Assert.Equal("derived-config-value", result);

		// Verify dependency works
		cache.Set("parent:config", "new-config-value");
		Assert.Null(cache.GetOrDefault<string>("derived:config"));
	}

	[Fact]
	public async Task GetOrSetAsyncWithDependencies_RegistersDependenciesCorrectly()
	{
		using var cache = new FusionCache(new FusionCacheOptions());

		// Set parent
		await cache.SetAsync("parent:settings", "settings-value");

		// Use GetOrSetAsync with dependencies
		var result = await cache.GetOrSetAsync("derived:settings",
			async _ => {
				await Task.Delay(10); // Simulate async work
				return "derived-settings-value";
			},
			options => options
				.SetDuration(TimeSpan.FromMinutes(5))
				.WithDependencies(DependsOn.Keys("parent:settings")));

		Assert.Equal("derived-settings-value", result);

		// Verify dependency works
		await cache.SetAsync("parent:settings", "new-settings-value");
		Assert.Null(await cache.GetOrDefaultAsync<string>("derived:settings"));
	}

	[Fact]
	public void RemoveEntry_RemovesDependenciesAndCascades()
	{
		using var cache = new FusionCache(new FusionCacheOptions());

		// Set up dependency chain
		cache.Set("root", "root-value");
		cache.Set("branch", "branch-value", options => options
			.WithDependencies(DependsOn.Keys("root")));
		cache.Set("leaf", "leaf-value", options => options
			.WithDependencies(DependsOn.Keys("branch")));

		// Verify all exist
		Assert.Equal("root-value", cache.GetOrDefault<string>("root"));
		Assert.Equal("branch-value", cache.GetOrDefault<string>("branch"));
		Assert.Equal("leaf-value", cache.GetOrDefault<string>("leaf"));

		// Remove branch - should cascade to leaf but not affect root
		cache.Remove("branch");

		Assert.Equal("root-value", cache.GetOrDefault<string>("root"));
		Assert.Null(cache.GetOrDefault<string>("branch"));
		Assert.Null(cache.GetOrDefault<string>("leaf"));
	}

	[Fact]
	public void CascadeDepthLimit_PreventsInfiniteRecursion()
	{
		var options = new FusionCacheOptions();
		options.Cascade.MaxCascadeDepth = 2;
		
		using var cache = new FusionCache(options);

		// Set up deep chain: level0 -> level1 -> level2 -> level3 -> level4
		cache.Set("level0", "value0");
		
		for (int i = 1; i <= 4; i++)
		{
			cache.Set($"level{i}", $"value{i}", options => options
				.WithDependencies(DependsOn.Keys($"level{i-1}")));
		}

		// Verify all exist
		for (int i = 0; i <= 4; i++)
		{
			Assert.Equal($"value{i}", cache.GetOrDefault<string>($"level{i}"));
		}

		// Update root - should only cascade to depth limit
		cache.Set("level0", "new-value0");

		// Level0 should be updated, level1 and level2 should be removed due to cascade
		// level3 and level4 should still exist due to depth limit
		Assert.Equal("new-value0", cache.GetOrDefault<string>("level0"));
		Assert.Null(cache.GetOrDefault<string>("level1"));
		Assert.Null(cache.GetOrDefault<string>("level2"));
		// These should still exist due to depth limit
		Assert.Equal("value3", cache.GetOrDefault<string>("level3"));
		Assert.Equal("value4", cache.GetOrDefault<string>("level4"));
	}

	[Fact]
	public void CascadeFanoutLimit_LimitsNumberOfInvalidations()
	{
		var options = new FusionCacheOptions();
		options.Cascade.MaxCascadeFanout = 5;
		
		using var cache = new FusionCache(options);

		// Set parent
		cache.Set("popular-parent", "popular-value");

		// Create many children depending on the same parent
		for (int i = 0; i < 10; i++)
		{
			cache.Set($"child:{i}", $"child-value-{i}", options => options
				.WithDependencies(DependsOn.Keys("popular-parent")));
		}

		// Verify all children exist
		for (int i = 0; i < 10; i++)
		{
			Assert.Equal($"child-value-{i}", cache.GetOrDefault<string>($"child:{i}"));
		}

		// Update parent - should only invalidate up to fanout limit
		cache.Set("popular-parent", "new-popular-value");

		// Some children should remain due to fanout limit
		var remainingChildren = 0;
		for (int i = 0; i < 10; i++)
		{
			if (cache.GetOrDefault<string>($"child:{i}") != null)
			{
				remainingChildren++;
			}
		}

		// Should have 5 children remaining (10 - 5 fanout limit)
		Assert.Equal(5, remainingChildren);
	}

	[Fact]
	public void BackplanePropagation_PropagatesCascadeInvalidation()
	{
		var memoryBackplane = new MemoryBackplane(Options.Create(new MemoryBackplaneOptions()), null);
		
		// Create two cache instances sharing the same backplane
		var cache1 = new FusionCache(new FusionCacheOptions { CacheName = "test-cache-1" });
		cache1.SetupBackplane(memoryBackplane);

		var cache2 = new FusionCache(new FusionCacheOptions { CacheName = "test-cache-2" });
		cache2.SetupBackplane(memoryBackplane);

		// Set parent in cache1
		cache1.Set("shared-parent", "parent-value");

		// Set child with dependency in cache2
		cache2.Set("shared-child", "child-value", options => options
			.SetDuration(TimeSpan.FromMinutes(10))
			.WithDependencies(DependsOn.Keys("shared-parent")));

		// Verify both caches have their entries
		Assert.Equal("parent-value", cache1.GetOrDefault<string>("shared-parent"));
		Assert.Equal("child-value", cache2.GetOrDefault<string>("shared-child"));

		// Update parent in cache1 - should propagate invalidation to cache2
		cache1.Set("shared-parent", "new-parent-value");

		// Give some time for backplane propagation
		Thread.Sleep(100);

		// Child in cache2 should be invalidated
		Assert.Null(cache2.GetOrDefault<string>("shared-child"));

		cache1.Dispose();
		cache2.Dispose();
	}

	[Fact]
	public async Task BackplanePropagationAsync_PropagatesCascadeInvalidation()
	{
		var memoryBackplane = new MemoryBackplane(Options.Create(new MemoryBackplaneOptions()), null);
		
		// Create two cache instances sharing the same backplane
		var cache1 = new FusionCache(new FusionCacheOptions { CacheName = "test-cache-1" });
		cache1.SetupBackplane(memoryBackplane);

		var cache2 = new FusionCache(new FusionCacheOptions { CacheName = "test-cache-2" });
		cache2.SetupBackplane(memoryBackplane);

		// Set parent in cache1
		await cache1.SetAsync("async-shared-parent", "parent-value");

		// Set child with dependency in cache2
		await cache2.SetAsync("async-shared-child", "child-value", options => options
			.SetDuration(TimeSpan.FromMinutes(10))
			.WithDependencies(DependsOn.Keys("async-shared-parent")));

		// Verify both caches have their entries
		Assert.Equal("parent-value", await cache1.GetOrDefaultAsync<string>("async-shared-parent"));
		Assert.Equal("child-value", await cache2.GetOrDefaultAsync<string>("async-shared-child"));

		// Update parent in cache1 - should propagate invalidation to cache2
		await cache1.SetAsync("async-shared-parent", "new-parent-value");

		// Give some time for backplane propagation
		await Task.Delay(100);

		// Child in cache2 should be invalidated
		Assert.Null(await cache2.GetOrDefaultAsync<string>("async-shared-child"));

		cache1.Dispose();
		cache2.Dispose();
	}

	[Fact]
	public void ComplexScenario_ProductCatalogWithDependencies()
	{
		using var cache = new FusionCache(new FusionCacheOptions());

		// Set up product hierarchy
		cache.Set("product:42", new { Id = 42, Name = "Laptop", CategoryId = 7 });
		cache.Set("category:7", new { Id = 7, Name = "Electronics" });

		// Set derived data with dependencies
		cache.Set("product:42:price", 999.99m, options => options
			.SetDuration(TimeSpan.FromMinutes(5))
			.WithDependencies(DependsOn.Keys("product:42")));

		cache.Set("product:42:inventory", 15, options => options
			.SetDuration(TimeSpan.FromMinutes(5))
			.WithDependencies(DependsOn.Keys("product:42")));

		cache.Set("category:7:top-products", new[] { 42, 35, 18 }, options => options
			.SetDuration(TimeSpan.FromMinutes(30))
			.WithDependencies(DependsOn.Keys("category:7")));

		// Verify all exist
		Assert.NotNull(cache.GetOrDefault<object>("product:42"));
		Assert.Equal(999.99m, cache.GetOrDefault<decimal>("product:42:price"));
		Assert.Equal(15, cache.GetOrDefault<int>("product:42:inventory"));
		Assert.NotNull(cache.GetOrDefault<int[]>("category:7:top-products"));

		// Update product - should invalidate price and inventory but not category data
		cache.Set("product:42", new { Id = 42, Name = "Gaming Laptop", CategoryId = 7 });

		Assert.NotNull(cache.GetOrDefault<object>("product:42"));
		Assert.Equal(default(decimal), cache.GetOrDefault<decimal>("product:42:price")); // Should be invalidated
		Assert.Equal(default(int), cache.GetOrDefault<int>("product:42:inventory")); // Should be invalidated  
		Assert.NotNull(cache.GetOrDefault<int[]>("category:7:top-products")); // Should remain

		// Update category - should invalidate top-products
		cache.Set("category:7", new { Id = 7, Name = "Consumer Electronics" });
		Assert.Null(cache.GetOrDefault<int[]>("category:7:top-products"));
	}

	[Fact]
	public void NoDependencies_DoesNotAffectNormalOperation()
	{
		using var cache = new FusionCache(new FusionCacheOptions());

		// Normal cache operations without dependencies should work as before
		cache.Set("normal:1", "value1");
		cache.Set("normal:2", "value2");

		Assert.Equal("value1", cache.GetOrDefault<string>("normal:1"));
		Assert.Equal("value2", cache.GetOrDefault<string>("normal:2"));

		// Updating one should not affect the other
		cache.Set("normal:1", "new-value1");

		Assert.Equal("new-value1", cache.GetOrDefault<string>("normal:1"));
		Assert.Equal("value2", cache.GetOrDefault<string>("normal:2"));
	}

	[Fact]
	public void DependencyBuilder_HandlesNullAndEmptyInputs()
	{
		// Should handle null arrays gracefully
		var builder1 = DependsOn.Keys(null!);

		// Should handle empty arrays
		var builder3 = DependsOn.Keys();

		// All should work without throwing exceptions
		Assert.NotNull(builder1);
		Assert.NotNull(builder2);
		Assert.NotNull(builder3);
		Assert.NotNull(builder4);
	}

	[Fact]
	public void CascadeOptions_DefaultValues_AreReasonable()
	{
		var options = new CascadeOptions();

		Assert.True(options.CascadeToL2);
		Assert.Equal(4, options.MaxCascadeDepth);
		Assert.Equal(5000, options.MaxCascadeFanout);
		Assert.False(options.NotifyChildFactory);
		Assert.Null(options.ParentValueComparer);
	}

	[Fact]
	public void WithDependencies_ThrowsOnNullInputs()
	{
		var options = new FusionCacheEntryOptions();

		Assert.Throws<ArgumentNullException>(() => 
			FusionCacheEntryOptionsExtensions.WithDependencies(null!, DependsOn.Keys("test")));

		Assert.Throws<ArgumentNullException>(() => 
			options.WithDependencies(null!));
	}

	[Fact]
	public void DependencyTracking_WorksWithExpiration()
	{
		using var cache = new FusionCache(new FusionCacheOptions());

		// Set parent
		cache.Set("expire-parent", "parent-value");

		// Set child with dependency and short expiration
		cache.Set("expire-child", "child-value", options => options
			.SetDuration(TimeSpan.FromMilliseconds(100))
			.WithDependencies(DependsOn.Keys("expire-parent")));

		// Wait for natural expiration
		Thread.Sleep(200);

		// Child should be naturally expired
		Assert.Null(cache.GetOrDefault<string>("expire-child"));

		// Parent still exists
		Assert.Equal("parent-value", cache.GetOrDefault<string>("expire-parent"));

		// Updating parent should not cause issues even though child is already expired
		cache.Set("expire-parent", "new-parent-value");
		Assert.Equal("new-parent-value", cache.GetOrDefault<string>("expire-parent"));
	}
}